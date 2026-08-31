using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Enqueues / voids outbound SMS &amp; WhatsApp reminder rows on the caller's scoped DbContext <b>after</b>
/// the appointment change has already committed, then leaves the actual sending to the connectivity-gated
/// dispatcher. Best-effort: every public method swallows its exceptions (logged at Error) so a reminder
/// failure can never fail/roll back the appointment create/update — the same contract as
/// <see cref="INotificationGenerator"/>.
/// </summary>
public class ReminderScheduler : IReminderScheduler
{
    private const string ReminderSubject = "Rappel de rendez-vous";
    private const string RecallSubject = "Relance patient";
    private const string FallbackClinicName = "votre cabinet";

    private readonly INotificationRepository _notifications;
    private readonly IClinicRepository _clinics;
    private readonly IPatientRepository _patients;
    private readonly IReminderSettingsProvider _settingsProvider;
    private readonly IVendorMessagingAvailability _messagingAvailability;
    private readonly IMessagingAllowanceRepository _allowances;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReminderScheduler> _logger;

    public ReminderScheduler(
        INotificationRepository notifications,
        IClinicRepository clinics,
        IPatientRepository patients,
        IReminderSettingsProvider settingsProvider,
        IVendorMessagingAvailability messagingAvailability,
        IMessagingAllowanceRepository allowances,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<ReminderScheduler> logger)
    {
        _notifications = notifications;
        _clinics = clinics;
        _patients = patients;
        _settingsProvider = settingsProvider;
        _messagingAvailability = messagingAvailability;
        _allowances = allowances;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _logger = logger;
    }

    public Task ScheduleForAppointmentAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default) =>
        SafelyAsync(appointmentId, "schedule", async () =>
        {
            await EnqueueRemindersAsync(
                clinicId, appointmentId, patientId, patientName, appointmentDateTimeUtc,
                voidedRowIds: new HashSet<Guid>(), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        });

    public Task RescheduleForAppointmentAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime newAppointmentDateTimeUtc,
        CancellationToken cancellationToken = default) =>
        SafelyAsync(appointmentId, "reschedule", async () =>
        {
            // ⚠️ The voided ids are threaded into the enqueue deliberately. `RemoveAsync` only *stages* the
            // delete, so the dedup read below still sees those rows (EF resolves the query back onto the same
            // tracked, Deleted instances) — and a dedup that counted them would skip re-creating exactly the
            // reminders this reschedule exists to replace.
            var voided = await VoidUnsentAsync(appointmentId, cancellationToken);
            await EnqueueRemindersAsync(
                clinicId, appointmentId, patientId, patientName, newAppointmentDateTimeUtc, voided, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        });

    public Task VoidForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default) =>
        SafelyAsync(appointmentId, "void", async () =>
        {
            await VoidUnsentAsync(appointmentId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        });

    public Task<RecallDispatchOutcome> ScheduleRecallAsync(
        Guid clinicId, Guid patientId, string patientName, string? reason,
        CancellationToken cancellationToken = default) =>
        SafelyRecallAsync(patientId, async () =>
        {
            // Gate at ENQUEUE, not at dispatch. A patient with no deliverable phone can never receive this,
            // so queuing it only fills the outbox with rows that fail hours later for a reason nobody acts on.
            var reachability = await ReachabilityOfAsync(patientId, cancellationToken);
            if (reachability == PatientReachability.ConsentRefused)
            {
                _logger.LogInformation(
                    "Skipped a recall for patient {PatientId}: the patient refused automated reminders.",
                    patientId);
                return RecallDispatchOutcome.ReminderConsentRefused;
            }

            if (reachability == PatientReachability.NoDeliverablePhone)
            {
                _logger.LogInformation(
                    "Skipped a recall for patient {PatientId}: no deliverable phone number.", patientId);
                return RecallDispatchOutcome.NoDeliverablePhone;
            }

            // Which channels + custom wording is per-clinic (its toggles/settings, else the install default).
            var settings = await _settingsProvider.ResolveAsync(clinicId, cancellationToken);

            // Enabled is not the same as sendable. A channel toggled on but missing its credentials leaves its
            // row Pending for ever at dispatch (NotConfigured is deliberately not a failure), so enqueuing on
            // it would keep the patient snoozed 30 days with no row that can ever resolve — the same silent
            // suppression AC-P3.2 exists to remove, and « Rappel envoyé … when no channel is configured » is
            // the audit's own wording. `SmsConfigured`/`WhatsAppConfigured` are the senders' own sendability
            // predicate, so this cannot disagree with what the dispatcher will do.
            var sendable = settings.EnabledChannels.Where(c => IsSendable(c, settings)).ToList();
            if (sendable.Count == 0)
            {
                _logger.LogInformation(
                    "Skipped a recall for patient {PatientId}: clinic {ClinicId} has no sendable reminder channel.",
                    patientId, clinicId);
                return RecallDispatchOutcome.NoChannelConfigured;
            }

            // US-5 — the forfait is checked at ENQUEUE for the recall path, unlike an appointment reminder.
            //
            // ⚠️ The two are gated in different places on purpose. An appointment reminder is queued days ahead and
            // measured against the forfait when it comes DUE, so a top-up in between still gets it out; a « Relancer »
            // is a gesture somebody just made, and the honest answer has to come back to their hand — which is what
            // AC-5.1 asks for and what stops the patient being snoozed 30 days with nothing sent (AC-5.2).
            //
            // ⚠️ Refused ONLY when WhatsApp is the sole sendable channel (AC-5.3). With SMS also sendable the relance
            // succeeds and both rows are queued: a channel being exhausted is not the same as having no channel, and
            // the WhatsApp row is simply held at dispatch by the same gate.
            if (sendable.Count == 1
                && sendable[0] == NotificationType.WhatsApp
                && await IsMessagingAllowanceExhaustedAsync(clinicId, cancellationToken))
            {
                _logger.LogInformation(
                    "Refused a recall for patient {PatientId}: clinic {ClinicId}'s WhatsApp forfait is spent and "
                    + "WhatsApp is its only sendable channel.",
                    patientId, clinicId);
                return RecallDispatchOutcome.MessagingAllowanceExhausted;
            }

            var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);
            var message = BuildRecallMessage(patientName, reason, clinic?.Name ?? FallbackClinicName);
            var sendTime = DateTime.UtcNow; // due now → dispatched on the next connectivity-gated tick

            foreach (var channel in sendable)
            {
                // A recall carries no appointment id and its own subject, so it is distinguishable from a
                // booking reminder in the outbox / reminder-status view. Every row of one « Relancer » click
                // shares this exact ScheduledFor, which is what lets the dispatcher recognise the batch and
                // decide the patient's state only once every channel has resolved (AC-P3.6).
                var recall = new Notification(
                    Guid.NewGuid(), channel, RecallSubject, message, sendTime,
                    appointmentId: null, patientId: patientId, clinicId: clinicId);
                await _notifications.AddAsync(recall, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return RecallDispatchOutcome.Enqueued;
        });

    // Adds one Pending reminder per (sendable channel × future lead tier). Stages the rows only (the caller
    // commits). No-op when no channel can send or the appointment is too close/past.
    private async Task EnqueueRemindersAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime appointmentDateTimeUtc,
        ISet<Guid> voidedRowIds,
        CancellationToken cancellationToken)
    {
        // Same enqueue-time gate as the recall path, through the same method: no consent or no deliverable
        // phone, no row. This path has no outcome to return — it is called while an appointment is being
        // saved — so the distinction lives only in the log line.
        var reachability = await ReachabilityOfAsync(patientId, cancellationToken);
        if (reachability != PatientReachability.Reachable)
        {
            _logger.LogInformation(
                "Skipped reminders for appointment {AppointmentId}: patient {PatientId} is {Reason}.",
                appointmentId,
                patientId,
                reachability == PatientReachability.ConsentRefused
                    ? "opted out of automated reminders"
                    : "without a deliverable phone number");
            return;
        }

        // AC-4: which channels to enqueue is per-clinic (its toggles where set, else the install default); the
        // full resolve also yields the per-clinic lead-time tiers + custom wording (else the install defaults).
        var settings = await _settingsProvider.ResolveAsync(clinicId, cancellationToken);

        // L3a — **sendability is checked here, not only on the recall path.** A channel toggled on but missing
        // its credentials produces a row that can never resolve: the dispatcher treats NotConfigured as
        // deliberately-not-a-failure, so it used to sit Pending at the front of the due scan for ever. Not
        // creating it is strictly better than creating one that has to be parked. (A channel configured *after*
        // this point is why `Notification.Unblock()` exists for the rows that do get parked.)
        var sendable = settings.EnabledChannels.Where(c => IsSendable(c, settings)).ToList();
        if (sendable.Count == 0)
        {
            _logger.LogInformation(
                "Skipped reminders for appointment {AppointmentId}: clinic {ClinicId} has no sendable reminder channel.",
                appointmentId, clinicId);
            return;
        }

        var appointmentUtc = NormalizeUtc(appointmentDateTimeUtc);
        var sendTimes = ReminderSchedule.ComputeSendTimesUtc(
            appointmentUtc,
            DateTime.UtcNow,
            settings.LeadTimeHours,
            RemindersConfig.MinLeadHours(_configuration),
            RemindersConfig.QuietHoursLocal(_configuration));
        if (sendTimes.Count == 0)
        {
            return;
        }

        // L3c idempotency — on **(appointment, channel, tier)**, and the tier's identity on the wire *is* its
        // send instant: two rows for one appointment and one channel at the same instant are the same message.
        // Without this the minutely job double-sends every tier the moment any path enqueues twice (a second
        // update, a Google-side move racing an in-app one). Rows of ANY status count, `Sent` included — the
        // whole point is not to re-create a message that has already gone out.
        var existing = (await _notifications.GetByAppointmentIdAsync(appointmentId, cancellationToken))
            .Where(n => !voidedRowIds.Contains(n.Id))
            .Select(n => (n.Type, n.ScheduledFor))
            .ToHashSet();

        var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);
        var message = BuildMessage(patientName, appointmentUtc, clinic?.Name ?? FallbackClinicName, settings.MessageTemplateBody);

        foreach (var channel in sendable)
        {
            foreach (var tier in sendTimes)
            {
                if (!existing.Add((channel, tier.SendAtUtc)))
                {
                    continue;
                }

                var reminder = new Notification(
                    Guid.NewGuid(), channel, ReminderSubject, message, tier.SendAtUtc,
                    appointmentId: appointmentId, patientId: patientId, clinicId: clinicId);
                await _notifications.AddAsync(reminder, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Can this channel actually send right now? Reads the same `*Configured` predicates the senders and the
    /// admin effective-status badge use, so enqueue, dispatch and UI cannot disagree.
    ///
    /// <para>Consulted by <b>both</b> enqueue paths since L3a. It used to be the recall path's alone, on the
    /// reasoning that an appointment reminder is enqueued hours ahead so a channel configured in the meantime
    /// should still send. That is true and it was not worth the cost: the row cannot resolve until then, and an
    /// unresolvable row sitting at the front of an oldest-first, batch-capped due scan starves the queue for the
    /// whole install. The « configured in the meantime » case is preserved by <c>Notification.Unblock()</c>
    /// instead, which recovers the rows already in the table.</para>
    /// </summary>
    private static bool IsSendable(NotificationType channel, ResolvedReminderSettings settings) => channel switch
    {
        NotificationType.SMS => settings.SmsConfigured,
        NotificationType.WhatsApp => settings.WhatsAppConfigured,
        _ => false, // Email/Both have no sender at all
    };

    /// <summary>
    /// Can this patient actually be reached? Uses <see cref="PhoneNumber.IsDeliverable"/> — the same predicate
    /// the senders apply — so the answer here and at dispatch can never disagree. A patient that has gone
    /// missing is treated as unreachable rather than throwing: this whole class is best-effort.
    /// </summary>
    private async Task<bool> HasDeliverablePhoneAsync(Guid patientId, CancellationToken cancellationToken)
        => await ReachabilityOfAsync(patientId, cancellationToken) == PatientReachability.Reachable;

    /// <summary>
    /// Why can — or cannot — this patient be sent an automated message? <b>One read, one answer, consulted by
    /// every enqueue path.</b>
    ///
    /// <para>⚠️ Consent is checked <b>here</b>, beside the phone, rather than at each call site. The two
    /// questions have the same shape (« is there any point queuing this? »), the same failure mode (a row that
    /// can never resolve, or worse, one that reaches somebody who said no), and the same cost if they disagree.
    /// Two enqueue paths exist today; a third would be written against this method, and
    /// <c>ReminderConsentCoverageTests</c> fails if one is written against the repository instead.</para>
    ///
    /// <para>A patient that has gone missing reads as unreachable rather than throwing — this whole class is
    /// best-effort — and a missing patient is also, correctly, not someone to message.</para>
    /// </summary>
    private async Task<PatientReachability> ReachabilityOfAsync(
        Guid patientId, CancellationToken cancellationToken)
    {
        var patient = await _patients.GetByIdAsync(patientId, cancellationToken);

        if (patient is null)
        {
            return PatientReachability.NoDeliverablePhone;
        }

        // ⚠️ Consent is asked FIRST, and the order is the point. A refusal is a standing instruction about the
        // person, not about the number on file: reporting « no valid phone » to a patient who refused would
        // send reception off to correct a number that is not the reason, and « corriger le numéro » is exactly
        // the action that would re-enrol them under the old behaviour.
        if (!patient.AcceptsReminders)
        {
            return PatientReachability.ConsentRefused;
        }

        return patient.PhoneNumber != null && PhoneNumber.IsDeliverable(patient.PhoneNumber.Value)
            ? PatientReachability.Reachable
            : PatientReachability.NoDeliverablePhone;
    }

    private enum PatientReachability
    {
        Reachable,
        NoDeliverablePhone,
        ConsentRefused,
    }

    // Removes every unsent reminder row for the appointment; Sent/Failed rows are left untouched.
    //
    // `Blocked` is unsent too, and dropping it here is required rather than tidy: a parked row still carries the
    // body and send time frozen at enqueue, so surviving a cancel or a move it would later be unblocked and sent
    // announcing a visit that is not happening, or one at the old hour.
    private async Task<HashSet<Guid>> VoidUnsentAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var voided = new HashSet<Guid>();
        var existing = await _notifications.GetByAppointmentIdAsync(appointmentId, cancellationToken);
        foreach (var reminder in existing.Where(n =>
                     n.Status is NotificationStatus.Pending or NotificationStatus.Blocked))
        {
            await _notifications.RemoveAsync(reminder, cancellationToken);
            voided.Add(reminder.Id);
        }

        return voided;
    }

    /// <summary>
    /// Is this cabinet's WhatsApp forfait spent for the current Tunisian month (AC-5.1)?
    ///
    /// <para><b>⚠️ « No allowance record » answers false here, and that is deliberate — it is the opposite of the
    /// dispatch gate's answer.</b> There the missing row is what the send is metered against, so sending would spend
    /// the vendor's credit line against a cabinet nothing is tracking; here the row is enqueued and then met by that
    /// same gate, which parks it under <c>MessagingAllowanceMissing</c> with its own sentence. Refusing at enqueue
    /// instead would tell a practice its forfait is « épuisé » when our own bookkeeping is what is missing — the exact
    /// conflation AC-4.3 exists to prevent.</para>
    ///
    /// <para>Where the deployment does not sell vendor messaging this reads nothing at all (EC-16).</para>
    /// </summary>
    private async Task<bool> IsMessagingAllowanceExhaustedAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        if (!_messagingAvailability.SellsVendorMessaging)
        {
            return false;
        }

        var month = await _allowances.GetMonthAsync(
            clinicId, ClinicClock.CurrentMonthKey(), cancellationToken);

        return month is not null && month.IsExhausted;
    }

    private async Task SafelyAsync(Guid appointmentId, string operation, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation} reminders for appointment {AppointmentId}.", operation, appointmentId);
        }
    }

    // Still best-effort — never throws back — but a fault now resolves to an explicit Failed outcome so the
    // caller can refuse the action instead of reporting a send that did not happen (AC-P3.1).
    private async Task<RecallDispatchOutcome> SafelyRecallAsync(Guid patientId, Func<Task<RecallDispatchOutcome>> work)
    {
        try
        {
            return await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule a recall for patient {PatientId}.", patientId);
            return RecallDispatchOutcome.Failed;
        }
    }

    // Recall wording: a short French "time for your next visit" nudge, distinct from the booking reminder.
    private static string BuildRecallMessage(string patientName, string? reason, string clinicName)
    {
        var motif = string.IsNullOrWhiteSpace(reason) ? "un contrôle" : reason.Trim();
        return $"Bonjour {patientName}, il est temps de programmer {motif} chez {clinicName}. Contactez-nous pour un rendez-vous.";
    }

    // Uses the clinic's custom wording when set (with {patient}/{date}/{clinic} placeholders), else the
    // built-in French default.
    private static string BuildMessage(string patientName, DateTime appointmentUtc, string clinicName, string? template)
    {
        var when = FormatFr(appointmentUtc);
        if (!string.IsNullOrWhiteSpace(template))
        {
            return template
                .Replace("{patient}", patientName)
                .Replace("{date}", when)
                .Replace("{clinic}", clinicName);
        }

        return $"Rappel : {patientName}, vous avez un rendez-vous le {when} chez {clinicName}.";
    }

    // One formatter, shared with the dispatcher's staleness check (ReminderMessage): the check compares the
    // body's stated moment against the appointment's current one, so a second copy of this format string here
    // would make every reminder read as stale.
    private static string FormatFr(DateTime utc) => ReminderMessage.FormatAppointmentMoment(utc);

    private static DateTime NormalizeUtc(DateTime dateTime) => dateTime.Kind switch
    {
        DateTimeKind.Utc => dateTime,
        DateTimeKind.Local => dateTime.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
    };

}
