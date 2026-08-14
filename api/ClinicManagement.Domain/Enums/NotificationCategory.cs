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
    BackupStale = 9,

    /// <summary>
    /// The cabinet's entitlement to record new work is about to end (<c>clinic-subscription</c> FR-5) — one row per
    /// threshold crossed, at 7, 3 and 1 day(s) before the end date and again on the day itself.
    ///
    /// <para>⚠️ <b>Four genuinely new rows, not one restated row</b>, which is the opposite of
    /// <see cref="StockExpiringSoon"/> and <see cref="BackupStale"/> beside it. Restating does not clear who has
    /// read it, so once the owner has read « 7 jours » the later three would stay read and never badge the bell
    /// again — i.e. AC-3.4's last three warnings would be invisible to exactly the person paying attention. The
    /// dedupe key is therefore <see cref="Entities.StaffNotification.SubscriptionThresholdDays"/>, a real column
    /// rather than a message prefix.</para>
    /// </summary>
    SubscriptionExpiring = 10,

    /// <summary>
    /// The cabinet is running out of its monthly WhatsApp reminder allowance
    /// (<c>vendor-whatsapp-messaging-quota</c> FR-6) — one row per threshold crossed, at 80, 95 and 100 %.
    ///
    /// <para>⚠️ <b>Three genuinely new rows, not one restated row</b>, for
    /// <see cref="SubscriptionExpiring"/>'s reason: restating does not clear who has read it, so once the owner has
    /// read « 80 % » the 95 % and 100 % rows would stay read and never badge the bell — and the 80 % one is the
    /// only one that could still have been acted on. The dedupe key is therefore
    /// (cabinet, month, threshold) over two real columns rather than a message prefix.</para>
    ///
    /// <para>⚠️ Unlike <see cref="SubscriptionExpiring"/>'s countdown this is crossed by a <i>send</i>, so it is
    /// evaluated where the counter is incremented as well as by the daily pass — 80 % is announced when it happens
    /// rather than the next morning.</para>
    /// </summary>
    MessagingAllowanceLow = 11,

    /// <summary>
    /// An administrator reset this account's second factor (<c>hosted-security-hardening</c> FR-1.4).
    ///
    /// <para>⚠️ <b>Targeted at the affected user, and it exists to make a quiet action loud.</b> Without it,
    /// stripping a colleague's protection is a silent step a stolen admin session could take before signing in
    /// as them. It stays <b>in-app</b> (and by e-mail): waking a dentist's lock screen adds nothing, because
    /// the action they must take — enrol again — happens at the machine, at their next sign-in.</para>
    /// </summary>
    SecondFactorReset = 12,

    /// <summary>
    /// Somebody downloaded the cabinet's <b>whole record</b> as one file
    /// (<c>hosted-security-hardening</c> FR-4.2, Stated Assumption 9).
    ///
    /// <para>⚠️ <b>Clinic-wide rather than targeted, which is a superset of what the spec asks for.</b> The feed
    /// has one shared row per event with an optional <i>single</i> target user, so « les administrateurs » is not
    /// expressible without a fan-out mechanism the model does not have. A clinic-wide row reaches every
    /// administrator — plus the rest of the staff, which is the right side to err on for an event that carries
    /// every patient of the practice out of the building on a laptop. The actor is excluded, as everywhere, so
    /// the person who pressed it is not told about themselves.</para>
    ///
    /// <para>In-app only: the export is already finished by the time this is written, so a lock-screen banner
    /// would announce something nobody can intervene in.</para>
    /// </summary>
    ClinicArchiveExported = 13,

    /// <summary>
    /// No archive of this cabinet has left the building for a while (<c>clinic-recovery-points</c>,
    /// <see cref="Entities.ClinicRecoveryPoint.ArchiveStaleAfterDays"/>).
    ///
    /// <para><b>It is about the copy the practice holds, not about the nightly recovery points.</b> Those live
    /// inside the deployment and die with it, so the fact worth nagging about is the one that survives a total loss —
    /// a file on the owner's own machine. That makes this the exact counterpart of
    /// <see cref="ClinicArchiveExported"/>: one says an archive left, this says none has.</para>
    ///
    /// <para>⚠️ <b>Distinct from <see cref="BackupStale"/></b>, which is about the machine-level <c>pg_dump</c> and
    /// therefore never fires on a hosted deployment at all (its job is not registered there, so there is nothing to
    /// go stale). Reading one as the other would leave the hosted profile — where this matters most — with no alert
    /// of either kind.</para>
    ///
    /// <para>An ensure/clear pair on <see cref="BackupStale"/>'s shape, not <see cref="SubscriptionExpiring"/>'s
    /// per-threshold one: there is a single fact here (« la dernière archive date du … ») rather than four
    /// escalating ones, so one row that restates is right and four unread rows would be noise.</para>
    ///
    /// <para>In-app only. It waits for whoever next sits at a keyboard — a lock-screen banner about downloading a
    /// multi-gigabyte file is a banner nobody can act on where they are standing.</para>
    /// </summary>
    ArchiveStale = 14
}
