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
    ArchiveStale = 14,

    /// <summary>
    /// Somebody else reset this account's <b>password</b> — an administrator of the clinic, or the vendor from the
    /// platform console.
    ///
    /// <para>⚠️ <b>Targeted at the affected user, and it exists to make a quiet action loud</b> — exactly
    /// <see cref="SecondFactorReset"/>'s reason, for the credential beside the one that category covers. Without
    /// it, taking over a colleague's account (from a stolen admin session, or by ringing support and telling a
    /// convincing story) leaves no trace the account's owner would ever see: their sessions simply end, which reads
    /// as an ordinary timeout.</para>
    ///
    /// <para>⚠️ <b>Not written when a person resets their own password</b> from the login screen. That is the owner
    /// acting, they are signed out of every session that could display this, and the confirmation they need goes to
    /// their mailbox instead — see <c>CompletePasswordResetCommand</c>.</para>
    ///
    /// <para>In-app (and by e-mail): what it asks for happens at the machine, at the next sign-in, so a lock-screen
    /// banner would add urgency to something nobody can act on from a phone.</para>
    /// </summary>
    PasswordReset = 15,

    /// <summary>
    /// No copy of this cabinet's <b>coffre</b> — the originals of files too large for the server
    /// (<c>clinic-file-vault</c>) — has left the practice's own machine for a while.
    ///
    /// <para>⚠️ <b>Distinct from <see cref="ArchiveStale"/>, and conflating them would leave exactly the hole this
    /// exists to close.</b> The archive carries the rows and the hosted blobs; a coffre original was never on the
    /// server, so no archive has ever contained one. A cabinet can therefore have a perfectly fresh archive and a
    /// coffre nobody has ever copied — and dental imaging carries a ten-to-twenty-year retention duty, so that is
    /// the state where a failed disk loses a decade of studies while every backup indicator reads green.</para>
    ///
    /// <para>⚠️ <b>The server cannot see the practice's disk</b>, so this is driven by what the shell reports
    /// (<c>POST /api/backup/vault-copy</c>) against <see cref="Entities.Clinic.LastVaultCopyAtUtc"/>. A cabinet whose
    /// coffre holds nothing is never nagged — there is nothing to lose yet, and a warning about an empty folder
    /// teaches an owner to ignore this one.</para>
    ///
    /// <para>An ensure/clear pair on <see cref="ArchiveStale"/>'s shape, and for its reason: one fact that restates
    /// rather than four escalating ones.</para>
    ///
    /// <para>In-app only. What it asks for is « branchez le disque et laissez la copie se faire », which happens at
    /// the machine holding the coffre.</para>
    /// </summary>
    VaultCopyStale = 16,

    /// <summary>
    /// A patient record was created by the Google Calendar import from an event title alone, and nobody has
    /// confirmed it yet (<c>calendar-import-review</c>).
    ///
    /// <para>⚠️ <b>Fire-once per patient, and clinic-wide.</b> Not an ensure/clear pair like
    /// <see cref="ArchiveStale"/>: each row is a distinct record needing attention, with its own deep link, so one
    /// restated row carrying a count would hide every patient but the number. And not targeted — the import path
    /// resolves no <c>DoctorId</c>, so there is no practitioner to address it to, and reception completes patient
    /// records as often as the dentist does.</para>
    ///
    /// <para>In-app only. What it asks for is a birth date and a telephone number typed into a fiche, which happens
    /// at a keyboard — and the record is provisional on the patient itself
    /// (<see cref="Entities.Patient.CalendarImportPendingReviewSince"/>), so the fact survives the bell being
    /// cleared. That column, not this row, is what the « À compléter » filter reads.</para>
    /// </summary>
    PatientImportedNeedsReview = 17
}
