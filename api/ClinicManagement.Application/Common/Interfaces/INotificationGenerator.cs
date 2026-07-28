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

    /// <summary>
    /// An outbound SMS/WhatsApp row reached <c>Failed</c> (AC-P3.7). Visible to <b>all</b> clinic staff — no
    /// actor exclusion — because the person who needs to pick up the phone is whoever is at the desk, not only
    /// an admin looking at the reminder-status card (AC-P3.8).
    ///
    /// <paramref name="appointmentId"/> is the discriminator, exactly as it is on the outbox row itself: a
    /// booking reminder always carries one and deep-links to that appointment; a recall never does and
    /// deep-links to the relance list, where <c>Patient.ClearRecallSnooze</c> has just put the patient back.
    /// Passing a flag alongside the id would let the two disagree.
    ///
    /// <paramref name="patientRequiresRecontact"/> adds the explicit « à recontacter » sentence for the recall
    /// case (AC-P3.5), which is only true once every channel of that send has failed.
    /// </summary>
    Task ReminderDeliveryFailedAsync(
        Guid clinicId, Guid? appointmentId, string patientName, string channel, string? reason,
        bool patientRequiresRecontact, CancellationToken cancellationToken = default);
}
