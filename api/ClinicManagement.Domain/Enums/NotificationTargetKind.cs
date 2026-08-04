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
    Recall = 3,

    /// <summary>
    /// The « Sauvegarde » section of « Paramètres » (L4d) — where the last successful backup, the schedule and
    /// the « Sauvegarder maintenant » button are. Like <see cref="Recall"/> it needs no id: the alert is about
    /// the clinic, and the action it demands is on one screen.
    /// </summary>
    BackupSettings = 4
}
