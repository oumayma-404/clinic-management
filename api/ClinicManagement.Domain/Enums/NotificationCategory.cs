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
    ReminderFailed = 7,

    /// <summary>
    /// A stock item holds a batch whose expiry falls inside the clinic's configured lead window
    /// (<see cref="Entities.Clinic.StockExpiryLeadDays"/>, default 30 days) — AC-P4.6. The counterpart to
    /// <see cref="LowStock"/>: low stock is "you will run out", this is "you will have to throw it away".
    /// </summary>
    StockExpiringSoon = 8,

    /// <summary>
    /// No successful backup for longer than <see cref="Entities.Clinic.BackupStaleAfterHours"/> (L4d).
    ///
    /// <para>Modelled as an <b>ensure/clear pair</b> like <see cref="StockExpiringSoon"/> and for the same
    /// reason: staleness is crossed by the <i>passage of time</i>, so the daily job re-evaluates the same fact
    /// every run and a fire-once call would write a duplicate row every day. It clears itself when a backup
    /// succeeds, which is what makes it a state rather than an accumulating pile of alerts.</para>
    /// </summary>
    BackupStale = 9
}
