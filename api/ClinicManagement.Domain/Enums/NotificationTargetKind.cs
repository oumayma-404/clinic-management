namespace ClinicManagement.Domain.Enums;

/// <summary>
/// The kind of record a staff notification deep-links to, so the frontend knows which screen to open.
/// </summary>
public enum NotificationTargetKind
{
    Appointment = 1,
    StockItem = 2
}
