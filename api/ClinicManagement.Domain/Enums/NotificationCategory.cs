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
    LowStock = 5
}
