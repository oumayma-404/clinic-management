using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Drains the OS-push outbox, on <see cref="NotificationJob"/>'s template — minutely, connectivity-gated,
/// bounded per tick and per clinic, committing per row.
///
/// <para><b>Eligibility is re-checked at dispatch, not trusted from enqueue</b>, and that is the difference
/// between this queue and one whose rows are just messages. A push bypasses every request-time check the app has:
/// it draws on a lock screen, on a device that may have changed hands, for an account that may have been
/// deactivated, about an appointment that may have been cancelled since. So each row re-reads its device, compares
/// the binding against the recipient it was queued for, and — for a reminder — re-reads the appointment.</para>
///
/// <para>And, on the identical terms as the reminder outbox, a cabinet that may not record new work pushes nothing:
/// its rows are <b>parked</b> through <see cref="OutboxSubscriptionGate"/> and released only once the entitlement is
/// (<c>clinic-subscription</c> FR-8).</para>
/// </summary>
public class PushDispatchJob
{
    private readonly IPushDeliveryRepository _deliveries;
    private readonly IDeviceRegistrationRepository _devices;
    private readonly IAppointmentRepository _appointments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IInternetProbe _internetProbe;
    private readonly IOsPushAvailability _availability;
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyDictionary<DevicePlatform, IPushSender> _senders;
    private readonly ISubscriptionPolicy _subscriptionPolicy;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<PushDispatchJob> _logger;

    public PushDispatchJob(
        IPushDeliveryRepository deliveries,
        IDeviceRegistrationRepository devices,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        IInternetProbe internetProbe,
        IOsPushAvailability availability,
        IConfiguration configuration,
        IEnumerable<IPushSender> senders,
        ISubscriptionPolicy subscriptionPolicy,
        IClinicSubscriptionRepository subscriptions,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<PushDispatchJob> logger)
    {
        _deliveries = deliveries;
        _devices = devices;
        _appointments = appointments;
        _unitOfWork = unitOfWork;
        _internetProbe = internetProbe;
        _availability = availability;
        _configuration = configuration;
        _senders = senders.ToDictionary(s => s.Platform);
        _subscriptionPolicy = subscriptionPolicy;
        _subscriptions = subscriptions;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    // Serialize runs: each send has a bounded timeout, but a batch can still outlast the minutely tick, and two
    // runs reading the same row as Pending would both send it.
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    public async Task DispatchQueuedPushes()
    {
        _auditActor.RunAs(nameof(PushDispatchJob));

        // US-2: the scan, the device reads and the appointment re-check all touch clinic-filtered tables across
        // every clinic. Without this the queue reads as empty and every notification stops, silently.
        _tenantScope.UseSystemWide("push dispatch reads every clinic's queued sends");

        // Same gate as the two other outbox dispatchers: with no egress every send would be a transient failure,
        // burning the retry budget of rows that are perfectly good. Leaving them Pending costs nothing.
        if (!await _internetProbe.IsInternetReachableAsync())
        {
            _logger.LogInformation("Skipping OS push dispatch — server has no internet connectivity.");
            return;
        }

        var batchSize = PushConfig.DispatchBatchSize(_configuration);
        var perClinicBound = PushConfig.PerClinicDispatchBound(_configuration);
        var maxAttempts = PushConfig.MaxAttempts(_configuration);
        var due = await _deliveries.GetDueForDispatchAsync(batchSize, perClinicBound, DateTime.UtcNow);

        // FR-8 — one gate for the whole tick, dispatch and review alike; see NotificationJob's own.
        var entitlements = new OutboxSubscriptionGate(
            _subscriptionPolicy, _subscriptions, ClinicClock.ClinicToday());

        foreach (var delivery in due)
        {
            try
            {
                await DispatchAsync(delivery, maxAttempts, entitlements);
            }
            catch (Exception ex)
            {
                // One row must never abort the batch. Left Pending; a later tick retries it.
                _logger.LogError(ex, "Unexpected error dispatching push {DeliveryId}", delivery.Id);
            }
        }

        await ReviewBlockedRowsAsync(batchSize, entitlements);
        await PurgeExpiredRowsAsync();
    }

    private async Task DispatchAsync(
        PushDelivery delivery, int maxAttempts, OutboxSubscriptionGate entitlements)
    {
        var device = await _devices.GetByIdAsync(delivery.DeviceRegistrationId);

        if (device == null || !device.IsActive)
        {
            await FailAsync(delivery, "Appareil désinscrit");
            return;
        }

        // The token was rebound to a colleague on a shared device, or the device moved clinic, since this row was
        // queued (AC-41). Delivering it now would put one user's notification on another's lock screen — which no
        // request-time check can prevent, because there is no request.
        if (!string.Equals(device.UserId, delivery.RecipientUserId, StringComparison.Ordinal)
            || device.ClinicId != delivery.ClinicId)
        {
            await FailAsync(delivery, "Appareil réattribué depuis la mise en file");
            return;
        }

        // A reminder announces a future visit, so unlike the four event categories it can be overtaken by events.
        // Re-checked here rather than deleted at cancellation time, which also covers the reschedule race.
        if (delivery.Category == NotificationCategory.Reminder
            && delivery.AppointmentId is Guid appointmentId
            && !await IsAppointmentStillActiveAsync(appointmentId))
        {
            await FailAsync(delivery, "Rendez-vous annulé ou introuvable — rappel non envoyé");
            return;
        }

        if (!_senders.TryGetValue(device.Platform, out var sender))
        {
            // No sender implements this platform at all. Nothing an operator configures changes that, so it is
            // parked rather than left Pending at the front of an oldest-first scan.
            await BlockAsync(
                delivery,
                OutboxBlockReason.ChannelUnsupported,
                $"Plateforme « {device.Platform} » non prise en charge");
            return;
        }

        if (!_availability.SupportsPush(device.Platform))
        {
            // The job only runs where at least one platform is sendable, so in practice this is the other
            // platform of a two-platform install with its credentials missing — operator-fixable.
            await BlockAsync(
                delivery,
                OutboxBlockReason.ChannelUnconfigured,
                _availability.UnavailableReason(device.Platform) ?? "Notifications système indisponibles");
            return;
        }

        // FR-8, EC-7 — as in the reminder outbox: the cabinet may not record new work, so the banner waits rather
        // than being sent or dropped, and the review pass releases it once the entitlement is extended.
        if (await entitlements.ReviewAsync(delivery.ClinicId) is { } parked)
        {
            await BlockAsync(delivery, parked.Reason, parked.Sentence);
            return;
        }

        var credentials = PushConfig.Resolve(_configuration, device.Platform);
        var message = new PushMessage(device.Token, delivery.Label, delivery.Category, delivery.AppointmentId);
        var result = await sender.SendAsync(message, credentials);

        switch (result.Outcome)
        {
            case PushSendOutcome.Sent:
                delivery.MarkAsSent(DateTime.UtcNow);
                await SaveAsync(delivery);
                break;

            case PushSendOutcome.TransientFailure:
                delivery.RecordFailedAttempt(result.Error, maxAttempts, DateTime.UtcNow);
                _logger.LogWarning(
                    "Push {DeliveryId} attempt {AttemptCount}/{MaxAttempts} failed: {Error}",
                    delivery.Id, delivery.AttemptCount, maxAttempts, result.Error);
                await SaveAsync(delivery);
                break;

            case PushSendOutcome.TokenInvalid:
                // AC-49 — terminal per DEVICE, not per message. Failing the row alone would leave every future
                // notification for an uninstalled app burning its whole retry budget, for ever.
                await FailAsync(delivery, result.Error ?? "Jeton d'appareil invalide");
                await DeactivateAsync(device);
                break;

            case PushSendOutcome.NotConfigured:
                await BlockAsync(
                    delivery,
                    OutboxBlockReason.ChannelUnconfigured,
                    $"Notifications {device.Platform} non configurées");
                break;
        }
    }

    /// <summary>
    /// Is the visit this reminder speaks for still happening? Mirrors <see cref="NotificationJob"/>'s own
    /// re-check, including which statuses count as gone.
    /// </summary>
    private async Task<bool> IsAppointmentStillActiveAsync(Guid appointmentId)
    {
        var appointment = await _appointments.GetByIdAsync(appointmentId);

        return appointment != null
               && appointment.Status != AppointmentStatus.Cancelled
               && appointment.Status != AppointmentStatus.NoShow;
    }

    /// <summary>
    /// AC-50's other half: a parked row returns to the queue once its platform is sendable, so the status is not a
    /// one-way door and the operator who finally supplies the credentials sees the backlog go out.
    ///
    /// <para>Runs after the dispatch loop and is bounded by the same batch size, so recovering a large backlog
    /// costs a tick at a time. The rows it unblocks are sent by the <i>next</i> tick, which is what keeps this pass
    /// out of the sender.</para>
    ///
    /// <para>⚠️ <b>The entitlement term is asked first</b>, exactly as in the reminder outbox and for the same reason:
    /// every platform check below is one a row parked for an expired cabinet would pass, so without it the banner
    /// would go out within a minute on a cabinet that has not paid (FR-8's named gap).</para>
    /// </summary>
    private async Task ReviewBlockedRowsAsync(int batchSize, OutboxSubscriptionGate entitlements)
    {
        try
        {
            var blocked = await _deliveries.GetBlockedForReviewAsync(batchSize);
            var unblocked = 0;

            foreach (var delivery in blocked)
            {
                if (await entitlements.ReviewAsync(delivery.ClinicId) is not null)
                {
                    continue;
                }

                var device = await _devices.GetByIdAsync(delivery.DeviceRegistrationId);
                if (device == null
                    || !device.IsActive
                    || !_senders.ContainsKey(device.Platform)
                    || !_availability.SupportsPush(device.Platform))
                {
                    continue;
                }

                if (delivery.Unblock(DateTime.UtcNow))
                {
                    await SaveAsync(delivery);
                    unblocked++;
                }
            }

            if (unblocked > 0)
            {
                _logger.LogInformation(
                    "Returned {Unblocked} blocked push row(s) to the queue: their platform is sendable again.",
                    unblocked);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to review blocked push rows.");
        }
    }

    /// <summary>
    /// Drops terminal rows past the retention window. Never a <c>Pending</c> or <c>Blocked</c> one — a parked row
    /// is the evidence of what is misconfigured. A failure here is swallowed: losing a housekeeping pass must not
    /// stop notifications going out.
    /// </summary>
    private async Task PurgeExpiredRowsAsync()
    {
        try
        {
            var retentionDays = PushConfig.RetentionDays(_configuration);
            var purged = await _deliveries.PurgeTerminalOlderThanAsync(
                DateTime.UtcNow.AddDays(-retentionDays));

            if (purged > 0)
            {
                _logger.LogInformation(
                    "Purged {Purged} push row(s) older than {RetentionDays} day(s).", purged, retentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to purge expired push rows.");
        }
    }

    private async Task FailAsync(PushDelivery delivery, string reason)
    {
        _logger.LogWarning("Push {DeliveryId} failed permanently: {Reason}", delivery.Id, reason);
        delivery.MarkAsFailed(reason, DateTime.UtcNow);
        await SaveAsync(delivery);
    }

    private async Task BlockAsync(PushDelivery delivery, OutboxBlockReason reason, string sentence)
    {
        _logger.LogInformation(
            "Push {DeliveryId} blocked ({Reason}): {Sentence}", delivery.Id, reason, sentence);
        delivery.MarkAsBlocked(reason, sentence, DateTime.UtcNow);
        await SaveAsync(delivery);
    }

    private async Task DeactivateAsync(DeviceRegistration device)
    {
        if (!device.Deactivate(DateTime.UtcNow))
        {
            return;
        }

        _logger.LogInformation(
            "Deactivated device registration {DeviceId}: the platform reports its token unregistered.", device.Id);
        await _devices.UpdateAsync(device);
        await _unitOfWork.SaveChangesAsync();
    }

    // Per-row commit, so a row leaves Pending on its own attempt and a crash mid-batch loses no progress.
    // With [DisableConcurrentExecution] this stops an overlapping tick re-sending. Delivery stays at-least-once:
    // a send that succeeds and then fails to commit is retried, which for a banner is the right direction.
    private async Task SaveAsync(PushDelivery delivery)
    {
        await _deliveries.UpdateAsync(delivery);
        await _unitOfWork.SaveChangesAsync();
    }
}
