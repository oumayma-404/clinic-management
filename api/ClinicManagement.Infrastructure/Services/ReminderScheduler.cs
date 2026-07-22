using System.Globalization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
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
    private const string FallbackClinicName = "votre clinique";

    // The app is Tunisia-targeted; appointment date/times are stored UTC but read best in local time.
    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly TimeZoneInfo TunisiaTimeZone = ResolveTunisiaTimeZone();

    private readonly INotificationRepository _notifications;
    private readonly IClinicRepository _clinics;
    private readonly IReminderSettingsProvider _settingsProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReminderScheduler> _logger;

    public ReminderScheduler(
        INotificationRepository notifications,
        IClinicRepository clinics,
        IReminderSettingsProvider settingsProvider,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        ILogger<ReminderScheduler> logger)
    {
        _notifications = notifications;
        _clinics = clinics;
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

    // Adds one Pending reminder per configured channel at the computed send time. Stages the rows only
    // (the caller commits). No-op when no channels are configured or the appointment is too close/past.
    private async Task EnqueueRemindersAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken)
    {
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
