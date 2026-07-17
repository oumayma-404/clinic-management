using System.Globalization;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common.Services;

/// <summary>
/// Writes in-app staff notifications (see <see cref="INotificationGenerator"/>). Persists on the caller's
/// scoped DbContext <b>after</b> the caller has already committed the core change, then broadcasts the
/// <c>"notifications"</c> realtime key. Best-effort: each public method wraps its work in try/catch and
/// swallows so a notification failure can never fail/roll back the core operation — but logs at Error with
/// the exception so a genuine bug stays visible (per the InternetProbe learning).
/// </summary>
public class NotificationGenerator : INotificationGenerator
{
    private const string RealtimeResourceKey = "notifications";

    // A reminder fires ~24h before the appointment; nothing is scheduled inside this window.
    private static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(24);

    // The app is Tunisia-targeted; appointment date/times are stored UTC but read best in local time.
    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly TimeZoneInfo TunisiaTimeZone = ResolveTunisiaTimeZone();

    private readonly IStaffNotificationRepository _notifications;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<NotificationGenerator> _logger;

    public NotificationGenerator(
        IStaffNotificationRepository notifications,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtimeNotifier,
        ILogger<NotificationGenerator> logger)
    {
        _notifications = notifications;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task AppointmentCreatedAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.AppointmentCreated,
                "Nouveau rendez-vous",
                $"Nouveau rendez-vous pour {patientName} le {FormatFr(appointmentDateTimeUtc)}.",
                DateTime.UtcNow,
                NotificationTargetKind.Appointment,
                actorUserId: actorUserId,
                appointmentId: appointmentId);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // immediately visible → refetch is worthwhile
        }, cancellationToken);
    }

    public async Task ScheduleAppointmentReminderAsync(
        Guid clinicId, Guid appointmentId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var dueTime = appointmentDateTimeUtc - ReminderLeadTime;
            // <24h out → the "appointment created" notification suffices; schedule nothing.
            if (dueTime <= DateTime.UtcNow)
            {
                return false;
            }

            var reminder = BuildReminder(clinicId, appointmentId, patientName, appointmentDateTimeUtc, dueTime);
            await _notifications.AddAsync(reminder, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // The reminder is future-dated (surfaces only once due), so nothing is visible in any feed
            // yet — no client refetch needed now.
            return false;
        }, cancellationToken);
    }

    public async Task AppointmentCancelledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.AppointmentCancelled,
                "Rendez-vous annulé",
                $"Le rendez-vous de {patientName} du {FormatFr(appointmentDateTimeUtc)} a été annulé.",
                DateTime.UtcNow,
                NotificationTargetKind.Appointment,
                actorUserId: actorUserId,
                appointmentId: appointmentId);
            await _notifications.AddAsync(notification, cancellationToken);

            // Suppress any pending reminder — a cancelled appointment must never remind.
            var reminder = await _notifications.GetReminderByAppointmentAsync(appointmentId, cancellationToken);
            if (reminder != null)
            {
                await _notifications.RemoveAsync(reminder, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // the cancellation notice is immediately visible
        }, cancellationToken);
    }

    public async Task AppointmentRescheduledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName,
        DateTime oldDateTimeUtc, DateTime newDateTimeUtc, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.AppointmentRescheduled,
                "Rendez-vous reporté",
                $"Le rendez-vous de {patientName} a été reporté du {FormatFr(oldDateTimeUtc)} au {FormatFr(newDateTimeUtc)}.",
                DateTime.UtcNow,
                NotificationTargetKind.Appointment,
                actorUserId: actorUserId,
                appointmentId: appointmentId);
            await _notifications.AddAsync(notification, cancellationToken);

            // Move the reminder to reflect the new time so no stale old-time reminder appears.
            var newDueTime = newDateTimeUtc - ReminderLeadTime;
            var reminder = await _notifications.GetReminderByAppointmentAsync(appointmentId, cancellationToken);
            if (reminder != null)
            {
                if (newDueTime > DateTime.UtcNow)
                {
                    reminder.MoveReminder(newDueTime, ReminderTitle, ReminderMessage(patientName, newDateTimeUtc));
                }
                else
                {
                    // New time is within the lead window → no reminder should exist anymore.
                    await _notifications.RemoveAsync(reminder, cancellationToken);
                }
            }
            else if (newDueTime > DateTime.UtcNow)
            {
                await _notifications.AddAsync(
                    BuildReminder(clinicId, appointmentId, patientName, newDateTimeUtc, newDueTime), cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // the reschedule notice is immediately visible
        }, cancellationToken);
    }

    public async Task LowStockAsync(
        Guid clinicId, Guid stockItemId, string itemName, int currentStock, int minimumStockLevel,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var notification = new StaffNotification(
                Guid.NewGuid(), clinicId, NotificationCategory.LowStock,
                "Stock faible",
                $"Stock faible : {itemName} ({currentStock}/{minimumStockLevel}).",
                DateTime.UtcNow,
                NotificationTargetKind.StockItem,
                actorUserId: null, // no single actor → visible to all staff
                stockItemId: stockItemId);

            await _notifications.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return true; // immediately visible to all staff
        }, cancellationToken);
    }

    public async Task EnsurePostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, string? doctorId, string patientName, DateTime appointmentEndUtc,
        CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var targetUserId = await ResolveTargetUserIdAsync(clinicId, doctorId, cancellationToken);
            var title = PostVisitReviewTitle;
            var message = PostVisitReviewMessage(patientName);

            var existing = await _notifications.GetPostVisitReviewByAppointmentAsync(appointmentId, cancellationToken);
            if (existing != null)
            {
                existing.MovePostVisitReview(appointmentEndUtc, targetUserId, title, message);
            }
            else
            {
                var notification = new StaffNotification(
                    Guid.NewGuid(), clinicId, NotificationCategory.PostVisitReview,
                    title, message,
                    appointmentEndUtc, // effective feed time = appointment end; surfaces only once it passes
                    NotificationTargetKind.Appointment,
                    actorUserId: null, // it is a prompt TO the doctor, so no actor is excluded
                    appointmentId: appointmentId,
                    stockItemId: null,
                    targetUserId: targetUserId);
                await _notifications.AddAsync(notification, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            // Future-dated (visible only once the end time passes) → no client refetch needed unless the
            // end is already in the past (appointment created/updated with an end already behind us).
            return appointmentEndUtc <= DateTime.UtcNow;
        }, cancellationToken);
    }

    public async Task CancelPostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, CancellationToken cancellationToken = default)
    {
        await SafelyAsync(clinicId, async () =>
        {
            var existing = await _notifications.GetPostVisitReviewByAppointmentAsync(appointmentId, cancellationToken);
            if (existing == null)
            {
                return false;
            }

            var wasVisible = existing.EffectiveFeedTime <= DateTime.UtcNow;
            await _notifications.RemoveAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return wasVisible; // only make clients refetch if it was actually showing
        }, cancellationToken);
    }

    // Resolves the post-visit target: the appointment's DoctorId (a Doctor id when set) → its linked User.
    // Any miss (no doctor id, unparsable, unknown doctor, or doctor with no linked user) → null = all staff.
    // The doctor must belong to the appointment's own clinic: the feed/pending queries filter on this clinic
    // AND the target user, so a foreign-clinic doctor's user would make the review invisible to everyone —
    // degrade a cross-clinic (or missing) resolution to the all-staff fallback instead.
    private async Task<string?> ResolveTargetUserIdAsync(Guid clinicId, string? doctorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(doctorId) || !Guid.TryParse(doctorId, out var doctorGuid))
        {
            return null;
        }

        var doctor = await _doctors.GetByIdAsync(doctorGuid, cancellationToken);
        if (doctor == null || doctor.ClinicId != clinicId)
        {
            return null;
        }

        return doctor.UserId;
    }

    // Runs the write and broadcasts the realtime key only when the write reports that something became
    // visible in the feed (returns true) — a future-dated reminder or a no-op schedule returns false, so
    // clients aren't made to refetch for no visible change. Best-effort: never throws to the caller (the
    // core operation is already committed); logs at Error so a genuine fault stays visible.
    private async Task SafelyAsync(Guid clinicId, Func<Task<bool>> write, CancellationToken cancellationToken)
    {
        try
        {
            if (await write())
            {
                await _realtimeNotifier.NotifyEntityChangedAsync(clinicId, RealtimeResourceKey, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate staff notification(s) for clinic {ClinicId}", clinicId);
        }
    }

    private const string PostVisitReviewTitle = "Compte rendu de visite";

    private static string PostVisitReviewMessage(string patientName) =>
        $"La visite de {patientName} est terminée. Ajoutez son dossier médical.";

    private const string ReminderTitle = "Rappel de rendez-vous";

    private static string ReminderMessage(string patientName, DateTime appointmentDateTimeUtc) =>
        $"Rappel : rendez-vous pour {patientName} le {FormatFr(appointmentDateTimeUtc)}.";

    private static StaffNotification BuildReminder(
        Guid clinicId, Guid appointmentId, string patientName, DateTime appointmentDateTimeUtc, DateTime dueTimeUtc) =>
        new(
            Guid.NewGuid(), clinicId, NotificationCategory.Reminder,
            ReminderTitle,
            ReminderMessage(patientName, appointmentDateTimeUtc),
            dueTimeUtc, // effective feed time = due time; surfaces only once dueTime <= now
            NotificationTargetKind.Appointment,
            actorUserId: null,
            appointmentId: appointmentId);

    private static string FormatFr(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TunisiaTimeZone);
        return local.ToString("dd/MM/yyyy 'à' HH:mm", FrCulture);
    }

    private static TimeZoneInfo ResolveTunisiaTimeZone()
    {
        // IANA id works cross-platform on .NET 8 (ICU); the Windows id is the fallback. If neither
        // resolves, use a fixed UTC+1 (Tunisia has no DST) so formatting still works.
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
