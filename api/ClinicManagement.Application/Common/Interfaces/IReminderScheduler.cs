namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Enqueues / voids outbound SMS &amp; WhatsApp appointment-reminder rows (the dormant <c>Notification</c>
/// outbox, revived). Called inline from the appointment command handlers <b>after</b> their own commit,
/// mirroring <see cref="INotificationGenerator"/>: every method is best-effort and never throws back —
/// a failure here must never fail or roll back the appointment create/update. Times are UTC.
///
/// One reminder <c>Notification</c> is created per configured channel at a send time computed from the
/// configured lead-time tiers; the actual sending is done later, connectivity-gated, by the dispatcher.
/// </summary>
public interface IReminderScheduler
{
    /// <summary>
    /// Enqueues one <c>Pending</c> reminder per configured channel for a newly-booked appointment, at the
    /// computed send time. No-op when no channels are configured or the appointment is too close/in the past.
    /// </summary>
    Task ScheduleForAppointmentAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids all unsent (<c>Pending</c>) reminders for an appointment and re-enqueues fresh ones for the new
    /// time (per the same rule). Reminders already <c>Sent</c> are left untouched.
    /// </summary>
    Task RescheduleForAppointmentAsync(
        Guid clinicId, Guid appointmentId, Guid patientId, string patientName, DateTime newAppointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids all unsent (<c>Pending</c>) reminders for an appointment so they never send (cancel / no-show).
    /// </summary>
    Task VoidForAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}
