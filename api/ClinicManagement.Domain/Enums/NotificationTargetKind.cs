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
    BackupSettings = 4,

    /// <summary>
    /// The « Abonnement » screen (<c>clinic-subscription</c> AC-3.4). Like <see cref="Recall"/> and
    /// <see cref="BackupSettings"/> it needs no id: the warning is about the cabinet, and everything it asks for
    /// — the end date, the tariff, how to pay and who to contact — is on that one screen.
    /// </summary>
    Subscription = 5,

    /// <summary>
    /// The « Forfait de rappels WhatsApp » section of « Rappels »
    /// (<c>vendor-whatsapp-messaging-quota</c> AC-3.3). Like the three above it needs <b>no id</b>: the warning is
    /// about the cabinet, and everything it asks for — what is left, when it renews, who to contact and which
    /// patients were not prevented — is on that one screen.
    /// </summary>
    MessagingAllowance = 6,

    /// <summary>
    /// The « Sécurité » screen (<c>hosted-security-hardening</c> FR-1.4/FR-1.5). Like <see cref="Recall"/>,
    /// <see cref="BackupSettings"/> and <see cref="Subscription"/> it carries <b>no id</b>: the alert is about
    /// this account, and everything it asks for is on that one screen.
    /// </summary>
    Security = 7
}
