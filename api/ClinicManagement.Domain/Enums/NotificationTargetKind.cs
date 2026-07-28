namespace ClinicManagement.Domain.Enums;

/// <summary>
/// The kind of record a staff notification deep-links to, so the frontend knows which screen to open.
/// </summary>
public enum NotificationTargetKind
{
    Appointment = 1,
    StockItem = 2,

    /// <summary>
    /// The « patients à relancer » list. Used by a failed <b>recall</b> (AC-P3.7): a recall carries no
    /// appointment, and the action it demands is re-contacting the patient — which is exactly what the
    /// relance list is for, and where AC-P3.5 puts them back. Needs no id of its own.
    /// </summary>
    Recall = 3
}
