namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Best-effort writer for the in-app staff notification feed. Called inline from command handlers
/// <b>after</b> their own commit. Every method is best-effort: it persists its own notification(s) and
/// broadcasts the <c>"notifications"</c> realtime key, but never throws back to the caller — a failure
/// here must never fail or roll back the core clinic operation (appointment/stock change). Times are UTC.
/// </summary>
public interface INotificationGenerator
{
    /// <summary>A new appointment was booked. Excludes the creating user from their own feed.</summary>
    Task AppointmentCreatedAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule the ~24h-before reminder for an appointment. No-op when the appointment is less than the
    /// reminder lead time away (the created notification already covers it). Visible to all staff.
    /// </summary>
    Task ScheduleAppointmentReminderAsync(
        Guid clinicId, Guid appointmentId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>An appointment was cancelled. Also suppresses any pending reminder for it. Actor-excluded.</summary>
    Task AppointmentCancelledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName, DateTime appointmentDateTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// An appointment was rescheduled. Also moves its reminder to reflect the new time (creates one if
    /// none and now far enough out; removes it if the new time is within the reminder lead time). Actor-excluded.
    /// </summary>
    Task AppointmentRescheduledAsync(
        Guid clinicId, Guid appointmentId, string? actorUserId, string patientName,
        DateTime oldDateTimeUtc, DateTime newDateTimeUtc, CancellationToken cancellationToken = default);

    /// <summary>A stock item crossed from not-low to low. Visible to all staff (no actor exclusion).</summary>
    Task LowStockAsync(
        Guid clinicId, Guid stockItemId, string itemName, int currentStock, int minimumStockLevel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a stock item holding a batch inside the clinic's expiry lead window carries exactly one live
    /// approaching-expiry alert, restated to that batch's date (AC-P4.6). Visible to all staff, like
    /// <see cref="LowStockAsync"/>, and deep-links to the item on the stock screen.
    ///
    /// <b>Ensure, not fire-once</b>, because expiry is crossed by the passage of time rather than by a write:
    /// the daily scan re-evaluates every item, so a fire-once call would write a duplicate row every day.
    /// Pair with <see cref="ClearStockExpiringSoonAsync"/> — an item whose expiring batch has been used up
    /// must stop being flagged, and clearing the row is also what lets the item's *next* batch alert.
    /// </summary>
    Task EnsureStockExpiringSoonAsync(
        Guid clinicId, Guid stockItemId, string itemName, DateTime earliestExpiryUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a stock item's approaching-expiry alert if one exists — the batch was consumed, discarded or
    /// re-dated, so the item is no longer expiring soon (AC-P4.6). No-op when there is nothing to clear.
    /// </summary>
    Task ClearStockExpiringSoonAsync(
        Guid clinicId, Guid stockItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the clinic carries exactly one live « sauvegarde ancienne » alert, restated to the moment of its
    /// last successful backup (L4d). Deep-links to the « Sauvegarde » section of « Paramètres ».
    ///
    /// <para><b>Ensure/clear, not fire-once</b>, for the identical reason as
    /// <see cref="EnsureStockExpiringSoonAsync"/>: staleness is crossed by the passage of time, so the daily job
    /// re-evaluates the same fact every run. A fire-once call would write one alert per day for ever, which is the
    /// fastest way to make the notification feed unreadable — and the feed is where the four other alerts that
    /// matter live.</para>
    ///
    /// <para><paramref name="lastSuccessUtc"/> is <c>null</c> on an install that has <b>never</b> backed up, and
    /// the wording differs: « aucune sauvegarde » on a brand-new clinic is not the same message as « la dernière
    /// remonte à trois jours », and firing the alarming version on a clinic created this morning is how an alert
    /// gets dismissed permanently on day one.</para>
    /// </summary>
    Task EnsureBackupStaleAsync(
        Guid clinicId, DateTime? lastSuccessUtc, int staleAfterHours,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the clinic's backup-staleness alert if one exists — a backup has just succeeded (L4d). No-op when
    /// there is nothing to clear, which is the overwhelmingly common case on a nightly run.
    /// </summary>
    Task ClearBackupStaleAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the cabinet carries exactly one warning row for <paramref name="thresholdDays"/> — 7, 3, 1 or 0 days
    /// before <paramref name="endsOn"/> (<c>clinic-subscription</c> AC-3.4). Visible to all staff (AC-3.7) and
    /// deep-links to « Abonnement ».
    ///
    /// <para><b>⚠️ Deduped per (cabinet, threshold), not per cabinet</b> — which is the opposite of
    /// <see cref="EnsureBackupStaleAsync"/> and <see cref="EnsureStockExpiringSoonAsync"/>, and deliberately so.
    /// Those keep one row and reword it; rewording <b>does not clear who has read it</b>, so once the owner has read
    /// « 7 jours », the « 3 jours », « 1 jour » and « aujourd'hui » restatements would stay read and never badge the
    /// bell again. AC-3.4 needs four genuinely new unread rows, so each threshold gets its own.</para>
    ///
    /// <para>Idempotent within a threshold: the daily pass finds the row and writes nothing (AC-3.5). It restates
    /// only when <paramref name="endsOn"/> itself has moved, since the message names that date.</para>
    /// </summary>
    Task EnsureSubscriptionWarningAsync(
        Guid clinicId, int thresholdDays, DateTime endsOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws <b>every</b> subscription-expiry warning the cabinet is carrying — the entitlement has moved back
    /// beyond the warning window, so the countdown is no longer true (FR-5). Clearing is also what <b>re-arms</b> the
    /// thresholds: a cabinet that renews and later approaches expiry again is warned all four times again.
    /// No-op when there is nothing to clear, which is the overwhelmingly common case on a daily pass.
    /// </summary>
    Task ClearSubscriptionWarningsAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the cabinet carries exactly one warning row for <paramref name="thresholdPercent"/> — 80, 95 or 100 % of
    /// <paramref name="allowance"/> in <paramref name="monthKey"/> (<c>vendor-whatsapp-messaging-quota</c> AC-3.1).
    /// Clinic-wide, no actor, deep-linking to « Rappels » (AC-3.3), and <b>never</b> an OS push (AC-3.4 — the category
    /// is classified <c>false</c> in <c>StaffNotificationRules.ReachesALockedPhone</c>).
    ///
    /// <para><b>⚠️ Deduped per (cabinet, month, threshold)</b>, on <see cref="EnsureSubscriptionWarningAsync"/>'s
    /// reasoning plus the month: three thresholds crossed in one afternoon must produce <b>three</b> unread rows, each
    /// badging the bell, and next month's 80 % must be a new row rather than a duplicate of this month's.</para>
    ///
    /// <para><b>⚠️ The message is derived from the threshold, the allowance and the month — never from the live
    /// consumed count</b> (AC-3.5). A count-bearing message differs on every send, so the ensure would restate the row
    /// and make every open browser refetch on every WhatsApp reminder the cabinet sends.</para>
    /// </summary>
    /// <param name="resetsOn">
    /// The day the forfait renews, named by the 100 % wording only. It is a fact about the <b>allowance</b> and must
    /// never be presented as a promise that the held reminders go out then (AC-4.2) —
    /// <c>MessagingRefusals</c> carries the same distinction for the parked rows.
    /// </param>
    Task EnsureMessagingAllowanceWarningAsync(
        Guid clinicId, string monthKey, int thresholdPercent, int allowance, DateTime resetsOn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws every WhatsApp-forfait warning the cabinet is carrying <b>except</b> the ones it still meets — the
    /// thresholds in <paramref name="keepThresholds"/> for the month <paramref name="keepMonthKey"/>.
    ///
    /// <para><b>⚠️ One reconciling call rather than two, because AC-3.6 and AC-3.7 are the same operation seen from two
    /// sides.</b> A grant that puts the cabinet back below a crossed threshold shrinks <c>keepThresholds</c>, so this
    /// month's now-untrue rows go — the bell never asserts two states of one month. A month rollover changes
    /// <c>keepMonthKey</c>, so <i>last</i> month's rows all go, which is what <b>re-arms</b> all three thresholds
    /// (a cabinet busy in August and busy again in September is warned both times). Two methods would be two
    /// call-site obligations at every writer, which is the <c>fixes-dont-propagate</c> shape.</para>
    ///
    /// <para>⚠️ It <b>keeps</b> the rows still met rather than clearing and re-ensuring: a rewritten row is a new row,
    /// and its read markers do not survive. That is the whole reason this family deduplicates on a column.</para>
    ///
    /// <para>Pass <c>keepMonthKey: null</c> and an empty set to withdraw everything.</para>
    /// </summary>
    Task ClearMessagingAllowanceWarningsAsync(
        Guid clinicId, string? keepMonthKey, IReadOnlyCollection<int> keepThresholds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a "post-visit review" notification for an appointment matches its current state — created if
    /// missing, otherwise moved. It becomes visible at the appointment's end (<paramref name="appointmentEndUtc"/> =
    /// start + duration; deferred visibility). The target user is resolved from <paramref name="doctorId"/>
    /// (→ Doctor → linked User): if a linked user exists, only they see it; otherwise all clinic staff do.
    /// Idempotent — safe to call on create, reschedule, duration/doctor change and reactivation.
    /// </summary>
    Task EnsurePostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, Guid? doctorId, string patientName, DateTime appointmentEndUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the post-visit review notification for an appointment, if one exists (cancel / fulfilled).</summary>
    Task CancelPostVisitReviewAsync(
        Guid clinicId, Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// An outbound SMS/WhatsApp row reached <c>Failed</c> (AC-P3.7). Visible to <b>all</b> clinic staff — no
    /// actor exclusion — because the person who needs to pick up the phone is whoever is at the desk, not only
    /// an admin looking at the reminder-status card (AC-P3.8).
    ///
    /// <paramref name="appointmentId"/> is the discriminator, exactly as it is on the outbox row itself: a
    /// booking reminder always carries one and deep-links to that appointment; a recall never does and
    /// deep-links to the relance list, where <c>Patient.ClearRecallSnooze</c> has just put the patient back.
    /// Passing a flag alongside the id would let the two disagree.
    ///
    /// <paramref name="patientRequiresRecontact"/> adds the explicit « à recontacter » sentence for the recall
    /// case (AC-P3.5), which is only true once every channel of that send has failed.
    /// </summary>
    Task ReminderDeliveryFailedAsync(
        Guid clinicId, Guid? appointmentId, string patientName, string channel, string? reason,
        bool patientRequiresRecontact, CancellationToken cancellationToken = default);
}
