using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Hangfire;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Connectivity-gated dispatcher for the SMS/WhatsApp appointment-reminder outbox. Runs minutely: when the
/// server has internet it sends every due <c>Pending</c> reminder via the sender matching its channel, with
/// the patient phone normalized to +216 E.164 and a bounded retry budget. Offline ⇒ it sends nothing and
/// leaves rows <c>Pending</c> without consuming any retry budget (mirrors the Google "non synchronisé" model).
/// </summary>
public class NotificationJob
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInternetProbe _internetProbe;
    private readonly IReminderSettingsProvider _settingsProvider;
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyDictionary<NotificationType, IReminderChannelSender> _senders;
    private readonly ILogger<NotificationJob> _logger;

    public NotificationJob(
        INotificationRepository notificationRepository,
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork,
        IInternetProbe internetProbe,
        IReminderSettingsProvider settingsProvider,
        IConfiguration configuration,
        IEnumerable<IReminderChannelSender> senders,
        ILogger<NotificationJob> logger)
    {
        _notificationRepository = notificationRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _internetProbe = internetProbe;
        _settingsProvider = settingsProvider;
        _configuration = configuration;
        _senders = senders.ToDictionary(s => s.Channel);
        _logger = logger;
    }

    // Serialize runs: a batch that outlasts the minutely tick (each send has a bounded timeout) must not be
    // picked up concurrently by the next tick, or two runs could read the same row as Pending and both send.
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    public async Task ProcessPendingNotifications()
    {
        // AC-5: the server (not a LAN client) is the source of truth for internet egress. Offline ⇒ send
        // nothing, leave rows Pending, and do NOT touch the retry count — the offline skip is free.
        if (!await _internetProbe.IsInternetReachableAsync())
        {
            _logger.LogInformation("Skipping reminder dispatch — server has no internet connectivity.");
            return;
        }

        var pendingNotifications = await _notificationRepository.GetPendingNotificationsAsync();
        var maxRetries = RemindersConfig.MaxRetries(_configuration);

        foreach (var notification in pendingNotifications)
        {
            try
            {
                await DispatchAsync(notification, maxRetries);
            }
            catch (Exception ex)
            {
                // A single row must never abort the batch. Leave it Pending; a later tick retries it.
                _logger.LogError(ex, "Unexpected error dispatching reminder {NotificationId}", notification.Id);
            }
        }
    }

    private async Task DispatchAsync(Notification notification, int maxRetries)
    {
        if (!_senders.TryGetValue(notification.Type, out var sender))
        {
            // No sender for this channel (e.g. a legacy Email row) — nothing to do; leave it Pending.
            _logger.LogDebug(
                "No reminder sender for channel {Channel}; leaving notification {NotificationId} pending.",
                notification.Type, notification.Id);
            return;
        }

        var patient = notification.PatientId.HasValue
            ? await _patientRepository.GetByIdAsync(notification.PatientId.Value)
            : null;
        if (patient == null)
        {
            await FailAsync(notification, "Patient introuvable");
            return;
        }

        var phone = ReminderPhone.ToE164(patient.PhoneNumber?.Value);
        if (phone == null)
        {
            await FailAsync(notification, "Numéro de téléphone invalide");
            return;
        }

        // AC-5: resolve the effective settings for this row's clinic (per-clinic override or per-install
        // fallback; a null ClinicId → per-install), then send under that clinic's identity/credentials.
        var settings = await _settingsProvider.ResolveAsync(notification.ClinicId);

        // A channel disabled (per-clinic toggle or install default) after this row was enqueued must not send.
        // Treat it like NotConfigured: send nothing and leave the row Pending (no Failed spam) — same contract
        // as a channel with missing credentials.
        if (!settings.EnabledChannels.Contains(notification.Type))
        {
            _logger.LogDebug(
                "Channel {Channel} is not enabled for notification {NotificationId}; leaving it pending.",
                notification.Type, notification.Id);
            return;
        }

        var result = await sender.SendAsync(phone, notification.Message, settings);
        switch (result.Outcome)
        {
            case ReminderSendOutcome.Sent:
                notification.MarkAsSent();
                await SaveAsync(notification);
                break;

            case ReminderSendOutcome.TransientFailure:
                // Keep Pending and retry on later ticks; only cross to Failed once the cap is reached.
                notification.RecordFailedAttempt(result.Error, maxRetries);
                if (notification.Status == NotificationStatus.Failed)
                {
                    _logger.LogWarning(
                        "Reminder {NotificationId} failed permanently after {RetryCount} attempt(s): {Error}",
                        notification.Id, notification.RetryCount, result.Error);
                }
                else
                {
                    _logger.LogWarning(
                        "Reminder {NotificationId} transient send failure (attempt {RetryCount}/{MaxRetries}): {Error}",
                        notification.Id, notification.RetryCount, maxRetries, result.Error);
                }
                await SaveAsync(notification);
                break;

            case ReminderSendOutcome.NotConfigured:
                // Channel enabled but credentials/template missing → send nothing, no Failed spam. The row
                // stays Pending so it sends once the operator configures the channel.
                break;
        }
    }

    private async Task FailAsync(Notification notification, string error)
    {
        _logger.LogWarning("Reminder {NotificationId} failed permanently: {Error}", notification.Id, error);
        notification.MarkAsFailed(error);
        await SaveAsync(notification);
    }

    // Commits after each row so a row leaves Pending on its own attempt's commit, and a crash mid-batch never
    // loses already-sent progress. Combined with [DisableConcurrentExecution] on the job, this stops one
    // tick's rows from being re-dispatched by an overlapping tick (AC-9). Delivery remains at-least-once,
    // not exactly-once: if a send succeeds but this commit then fails, the row stays Pending and a later tick
    // re-sends it — true dedup would need a provider idempotency key (out of scope).
    private async Task SaveAsync(Notification notification)
    {
        await _notificationRepository.UpdateAsync(notification);
        await _unitOfWork.SaveChangesAsync();
    }
}
