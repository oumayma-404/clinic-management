namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Kind of in-app staff notification. Drives the row icon and the deep-link target.
/// Distinct from <see cref="NotificationType"/>/<see cref="NotificationStatus"/>, which model the
/// separate (dormant) outbound email/SMS reminder pipeline.
/// </summary>
public enum NotificationCategory
{
    AppointmentCreated = 1,
    AppointmentCancelled = 2,
    AppointmentRescheduled = 3,
    Reminder = 4,
    LowStock = 5,
    PostVisitReview = 6,

    /// <summary>
    /// An outbound SMS/WhatsApp reminder or recall reached <see cref="NotificationStatus.Failed"/>
    /// (AC-P3.7). Without this the outbox's failures were visible only in the admin reminder-status card,
    /// so the secretary who booked the appointment never learned the patient was not reached.
    /// </summary>
    ReminderFailed = 7
}
