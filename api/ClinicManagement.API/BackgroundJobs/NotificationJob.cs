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
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInternetProbe _internetProbe;
    private readonly IReminderSettingsProvider _settingsProvider;
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyDictionary<NotificationType, IReminderChannelSender> _senders;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly ILogger<NotificationJob> _logger;

    public NotificationJob(
        INotificationRepository notificationRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        IInternetProbe internetProbe,
        IReminderSettingsProvider settingsProvider,
        IConfiguration configuration,
        IEnumerable<IReminderChannelSender> senders,
        INotificationGenerator notificationGenerator,
        ILogger<NotificationJob> logger)
    {
        _notificationRepository = notificationRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _internetProbe = internetProbe;
        _settingsProvider = settingsProvider;
        _configuration = configuration;
        _senders = senders.ToDictionary(s => s.Channel);
        _notificationGenerator = notificationGenerator;
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

        // AC-P4.31 — bounded, like EInvoiceOutboxJob. The scan was unbounded against a table nothing had ever
        // purged, so one backlog could make a single tick run for minutes while holding this job's
        // [DisableConcurrentExecution] lock and starving every later tick.
        var batchSize = RemindersConfig.DispatchBatchSize(_configuration);
        var pendingNotifications = await _notificationRepository.GetPendingNotificationsAsync(batchSize);
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

        await PurgeExpiredRowsAsync();
    }

    /// <summary>
    /// AC-P4.32 — drops terminal rows past the retention window. This table had <b>no</b> purge of any kind, so
    /// it grew forever; every reminder ever sent was still there.
    ///
    /// Runs <b>after</b> the dispatch loop, not before (EC-13): purging first would compete with the rows this
    /// same tick is about to read. It only ever deletes <c>Sent</c>/<c>Failed</c> — never a <c>Pending</c> row
    /// (AC-P4.34) — and a failure here is swallowed, because losing a housekeeping pass must not stop reminders
    /// going out.
    /// </summary>
    private async Task PurgeExpiredRowsAsync()
    {
        try
        {
            var retentionDays = RemindersConfig.RetentionDays(_configuration);
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var purged = await _notificationRepository.PurgeTerminalOlderThanAsync(cutoff);

            if (purged > 0)
            {
                _logger.LogInformation(
                    "Purged {Purged} reminder row(s) older than {RetentionDays} day(s).", purged, retentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge expired reminder rows.");
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

        // AC-4: never send a reminder for an appointment that is no longer active at send time. The cancel/
        // no-show path voids reminders (ReminderScheduler.VoidForAppointmentAsync); this re-check is a safety
        // net for a void failure or a cancel-vs-tick race — drop the row terminally so it is not re-sent.
        if (notification.AppointmentId.HasValue)
        {
            var appointment = await _appointmentRepository.GetByIdAsync(notification.AppointmentId.Value);
            if (appointment == null
                || appointment.Status == AppointmentStatus.Cancelled
                || appointment.Status == AppointmentStatus.NoShow)
            {
                await FailAsync(notification, "Rendez-vous annulé ou introuvable — rappel non envoyé");
                return;
            }
        }

        var patient = notification.PatientId.HasValue
            ? await _patientRepository.GetByIdAsync(notification.PatientId.Value)
            : null;
        if (patient == null)
        {
            await FailAsync(notification, "Patient introuvable");
            await SurfaceFailureAsync(notification, UnknownPatientName, "Patient introuvable");
            return;
        }

        var phone = ReminderPhone.ToE164(patient.PhoneNumber?.Value);
        if (phone == null)
        {
            await FailAsync(notification, "Numéro de téléphone invalide");
            await SurfaceFailureAsync(notification, patient.GetFullName(), "Numéro de téléphone invalide");
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
                    await SaveAsync(notification);
                    // Only once the retry budget is spent — surfacing every transient attempt would put a
                    // notification in the feed for a reminder that goes on to send fine on the next tick.
                    await SurfaceFailureAsync(notification, patient.GetFullName(), result.Error);
                }
                else
                {
                    _logger.LogWarning(
                        "Reminder {NotificationId} transient send failure (attempt {RetryCount}/{MaxRetries}): {Error}",
                        notification.Id, notification.RetryCount, maxRetries, result.Error);
                    await SaveAsync(notification);
                }
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

    /// <summary>
    /// AC-P3.7/3.8 — put a failed row in front of the staff who can act on it, not only in the admin
    /// reminder-status card. AC-P3.11 — best-effort in the strict sense: this method never throws, so a feed
    /// write can neither abort the batch nor cause the row to be dispatched a second time.
    ///
    /// Deliberately <b>not</b> called for the cancelled/no-show void above. That row failing is the correct
    /// suppression of a reminder for a visit that is not happening, and
    /// <c>NotificationGenerator.AppointmentCancelledAsync</c> has already told the staff — a second
    /// « Rappel non envoyé » row would be exactly the noise that makes a feed stop being read.
    /// </summary>
    private async Task SurfaceFailureAsync(Notification notification, string patientName, string? reason)
    {
        try
        {
            // Legacy/global rows predate per-clinic settings and carry no ClinicId, so there is no clinic
            // feed to write to (and no clinic whose patient could be un-snoozed).
            if (notification.ClinicId is not Guid clinicId)
            {
                return;
            }

            var requiresRecontact = false;
            if (notification.AppointmentId is null && notification.PatientId is Guid patientId)
            {
                requiresRecontact = await TryReturnPatientToRecallListAsync(
                    clinicId, patientId, notification.ScheduledFor);
            }

            await _notificationGenerator.ReminderDeliveryFailedAsync(
                clinicId, notification.AppointmentId, patientName, ChannelLabel(notification.Type),
                reason, requiresRecontact);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Failed to surface the delivery failure of reminder {NotificationId}.", notification.Id);
        }
    }

    /// <summary>
    /// AC-P3.5/3.6 — <b>enqueuing is not sending.</b> A « Relancer » click stamps « contacté » and snoozes the
    /// patient 30 days at enqueue time; if the message then never arrives, that snooze is the original defect
    /// one step later. So once every channel of that one send has reached <c>Failed</c>, undo it and the patient
    /// returns to the relance list.
    ///
    /// The batch check is what makes a partial send a <i>stated</i> state rather than an implicit one: a
    /// sibling row still <c>Pending</c> means this send has not resolved yet (a later tick decides), and a
    /// sibling <c>Sent</c> means the patient really was reached on that channel, so the snooze stands.
    /// Returns whether the patient was actually put back, which is what the feed row then says out loud.
    /// </summary>
    private async Task<bool> TryReturnPatientToRecallListAsync(
        Guid clinicId, Guid patientId, DateTime scheduledFor)
    {
        var batch = await _notificationRepository.GetRecallBatchAsync(patientId, scheduledFor);
        if (batch.Any(n => n.Status != NotificationStatus.Failed))
        {
            return false;
        }

        var patient = await _patientRepository.GetByIdAsync(patientId);
        if (patient == null || patient.ClinicId != clinicId || !patient.ClearRecallSnooze())
        {
            return false;
        }

        await _patientRepository.UpdateAsync(patient);
        await _unitOfWork.SaveChangesAsync();
        _logger.LogInformation(
            "Patient {PatientId} returned to the recall list: every channel of their recall failed.", patientId);
        return true;
    }

    // French channel label for the staff feed. "SMS" and "WhatsApp" are the two channels a reminder row can
    // actually carry; the legacy Email/Both members have no sender, so such a row never reaches this method.
    private static string ChannelLabel(NotificationType type) => type switch
    {
        NotificationType.SMS => "SMS",
        NotificationType.WhatsApp => "WhatsApp",
        _ => type.ToString(),
    };

    private const string UnknownPatientName = "un patient introuvable";

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
