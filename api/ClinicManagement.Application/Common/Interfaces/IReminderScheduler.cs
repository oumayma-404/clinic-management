namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Enqueues / voids outbound SMS &amp; WhatsApp appointment-reminder rows (the dormant <c>Notification</c>
/// outbox, revived). Called inline from the appointment command handlers <b>after</b> their own commit,
/// mirroring <see cref="INotificationGenerator"/>: every method is best-effort and never throws back —
/// a failure here must never fail or roll back the appointment create/update. Times are UTC.
///
/// One reminder <c>Notification</c> is created per <b>(configured channel × future lead tier)</b>; the actual
/// sending is done later, connectivity-gated, by the dispatcher.
///
/// <para>⚠️ « per tier » is since L3c. It used to be one row per channel at the <i>single</i> largest future
/// tier, with every other tier silently discarded — while the settings screen invited « Ex. 24, 6 ». For a
/// no-show problem the 6 h nudge is the one that works.</para>
/// </summary>
public interface IReminderScheduler
{
    /// <summary>
    /// Enqueues one <c>Pending</c> reminder per (sendable channel × future lead tier) for a newly-booked
    /// appointment. No-op when no channel can actually send, or the appointment is too close/in the past.
    ///
    /// <para>Idempotent on <b>(appointment, channel, tier)</b>: calling it twice for the same booking adds
    /// nothing, which is what stops the minutely dispatcher double-sending every tier.</para>
    /// </summary>
    Task ScheduleForAppointmentAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids all unsent (<c>Pending</c> / <c>Blocked</c>) reminders for an appointment and re-enqueues fresh ones
    /// for the new time (per the same rule). Reminders already <c>Sent</c> are left untouched.
    /// </summary>
    Task RescheduleForAppointmentAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime newAppointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids all unsent (<c>Pending</c> / <c>Blocked</c>) reminders for an appointment so they never send
    /// (cancel / no-show). A <c>Blocked</c> row is dropped too: it still carries the body frozen at enqueue, so
    /// surviving here it could later be unblocked and announce a visit that is not happening.
    /// </summary>
    Task VoidForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues one recall (« relance ») message per configured channel for a patient, due immediately (the
    /// next dispatcher tick sends it, connectivity-gated). Distinct from booking reminders (its own subject),
    /// carries no appointment id. Best-effort — never throws back.
    ///
    /// Unlike the appointment methods this one <b>reports its outcome</b> (AC-P3.1): the recall is a
    /// user-initiated action whose whole point is that the patient gets a message, so its caller has to be
    /// able to refuse rather than snooze the patient for 30 days having queued nothing.
    /// </summary>
    Task<RecallDispatchOutcome> ScheduleRecallAsync(
        Guid clinicId, Guid patientId, string patientName, string? reason,
        CancellationToken cancellationToken = default);
}
