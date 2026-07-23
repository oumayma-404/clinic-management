namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Best-effort writer for the in-app staff notification feed. Called inline from command handlers
/// <b>after</b> their own commit. Every method is best-effort: it persists its own notification(s) and
/// broadcasts the <c>"notifications"</c> realtime key, but never throws back to the caller — a failure
/// here must never fail or roll back the core clinic operation (appointment/stock change). Times are UTC.
/// </summary>
public interface INotificationGenerator
{
    /// <summary>A new appointment was booked. Excludes the creating user from their own feed.</summary>
    Task AppointmentCreatedAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule the ~24h-before reminder for an appointment. No-op when the appointment is less than the
    /// reminder lead time away (the created notification already covers it). Visible to all staff.
    /// </summary>
    Task ScheduleAppointmentReminderAsync(
        Guid clinicId, Guid appointmentId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>An appointment was cancelled. Also suppresses any pending reminder for it. Actor-excluded.</summary>
    Task AppointmentCancelledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An appointment was rescheduled. Also moves its reminder to reflect the new time (creates one if
    /// none and now far enough out; removes it if the new time is within the reminder lead time). Actor-excluded.
    /// </summary>
    Task AppointmentRescheduledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName,
        DateTime oldDateTimeUtc, DateTime newDateTimeUtc, CancellationToken cancellationToken = default);

    /// <summary>A stock item crossed from not-low to low. Visible to all staff (no actor exclusion).</summary>
    Task LowStockAsync(
        Guid clinicId, Guid stockItemId, string itemName, int currentStock, int minimumStockLevel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a "post-visit review" notification for an appointment matches its current state — created if
    /// missing, otherwise moved. It becomes visible at the appointment's end (<paramref name="appointmentEndUtc"/> =
    /// start + duration; deferred visibility). The target user is resolved from <paramref name="doctorId"/>
    /// (→ Doctor → linked User): if a linked user exists, only they see it; otherwise all clinic staff do.
    /// Idempotent — safe to call on create, reschedule, duration/doctor change and reactivation.
    /// </summary>
    Task EnsurePostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, Guid? doctorId, string patientName, DateTime appointmentEndUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the post-visit review notification for an appointment, if one exists (cancel / fulfilled).</summary>
    Task CancelPostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, CancellationToken cancellationToken = default);
}
