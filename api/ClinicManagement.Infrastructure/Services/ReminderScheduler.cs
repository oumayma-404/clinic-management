using System.Globalization;
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
    private const string FallbackClinicName = "votre clinique";

    // The app is Tunisia-targeted; appointment date/times are stored UTC but read best in local time.
    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly TimeZoneInfo TunisiaTimeZone = ResolveTunisiaTimeZone();

    private readonly INotificationRepository _notifications;
    private readonly IClinicRepository _clinics;
    private readonly IPatientRepository _patients;
    private readonly IReminderSettingsProvider _settingsProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReminderScheduler> _logger;

    public ReminderScheduler(
        INotificationRepository notifications,
        IClinicRepository clinics,
        IPatientRepository patients,
        IReminderSettingsProvider settingsProvider,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<ReminderScheduler> logger)
    {
        _notifications = notifications;
        _clinics = clinics;
        _patients = patients;
        _settingsProvider = settingsProvider;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _logger = logger;
    }

    public Task ScheduleForAppointmentAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default) =>
        SafelyAsync(appointmentId, "schedule", async () =>
        {
            await EnqueueRemindersAsync(clinicId, appointmentId, patientId, patientName, appointmentDateTimeUtc, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        });

    public Task RescheduleForAppointmentAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime newAppointmentDateTimeUtc,
        CancellationToken cancellationToken = default) =>
        SafelyAsync(appointmentId, "reschedule", async () =>
        {
            await VoidUnsentAsync(appointmentId, cancellationToken);
            await EnqueueRemindersAsync(clinicId, appointmentId, patientId, patientName, newAppointmentDateTimeUtc, cancellationToken);
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
            if (!await HasDeliverablePhoneAsync(patientId, cancellationToken))
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

    // Adds one Pending reminder per configured channel at the computed send time. Stages the rows only
    // (the caller commits). No-op when no channels are configured or the appointment is too close/past.
    private async Task EnqueueRemindersAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken)
    {
        // Same enqueue-time gate as the recall path: no deliverable phone, no row.
        if (!await HasDeliverablePhoneAsync(patientId, cancellationToken))
        {
            _logger.LogInformation(
                "Skipped reminders for appointment {AppointmentId}: patient {PatientId} has no deliverable phone number.",
                appointmentId, patientId);
            return;
        }

        // AC-4: which channels to enqueue is per-clinic (its toggles where set, else the install default); the
        // full resolve also yields the per-clinic lead-time tiers + custom wording (else the install defaults).
        var settings = await _settingsProvider.ResolveAsync(clinicId, cancellationToken);
        if (settings.EnabledChannels.Count == 0)
        {
            return;
        }

        var appointmentUtc = NormalizeUtc(appointmentDateTimeUtc);
        var sendTime = ReminderSchedule.ComputeSendTimeUtc(
            appointmentUtc,
            DateTime.UtcNow,
            settings.LeadTimeHours,
            RemindersConfig.MinLeadHours(_configuration));
        if (sendTime == null)
        {
            return;
        }

        var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);
        var message = BuildMessage(patientName, appointmentUtc, clinic?.Name ?? FallbackClinicName, settings.MessageTemplateBody);

        foreach (var channel in settings.EnabledChannels)
        {
            var reminder = new Notification(
                Guid.NewGuid(), channel, ReminderSubject, message, sendTime.Value,
                appointmentId: appointmentId, patientId: patientId, clinicId: clinicId);
            await _notifications.AddAsync(reminder, cancellationToken);
        }
    }

    /// <summary>
    /// Can this channel actually send right now? Reads the same `*Configured` predicates the senders and the
    /// admin effective-status badge use, so enqueue, dispatch and UI cannot disagree. Only the recall path
    /// consults this: an appointment reminder is enqueued hours ahead, so a channel configured in the meantime
    /// should still send — a recall is due *now* and its caller is waiting for a yes/no.
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
    {
        var patient = await _patients.GetByIdAsync(patientId, cancellationToken);
        return patient?.PhoneNumber != null && PhoneNumber.IsDeliverable(patient.PhoneNumber.Value);
    }

    // Removes every unsent (Pending) reminder row for the appointment; Sent rows are left untouched.
    private async Task VoidUnsentAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var existing = await _notifications.GetByAppointmentIdAsync(appointmentId, cancellationToken);
        foreach (var reminder in existing.Where(n => n.Status == NotificationStatus.Pending))
        {
            await _notifications.RemoveAsync(reminder, cancellationToken);
        }
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

    private static string FormatFr(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TunisiaTimeZone);
        return local.ToString("dd/MM/yyyy 'à' HH:mm", FrCulture);
    }

    private static DateTime NormalizeUtc(DateTime dateTime) => dateTime.Kind switch
    {
        DateTimeKind.Utc => dateTime,
        DateTimeKind.Local => dateTime.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
    };

    private static TimeZoneInfo ResolveTunisiaTimeZone()
    {
        // IANA id works cross-platform on .NET 8 (ICU); the Windows id is the fallback. If neither resolves,
        // use a fixed UTC+1 (Tunisia has no DST) so formatting still works.
        foreach (var id in new[] { "Africa/Tunis", "W. Central Africa Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Tunisia", TimeSpan.FromHours(1), "Tunisia", "Tunisia");
    }
}
