using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for the in-app staff notification feed. Reads are clinic- and viewer-scoped; the
/// due-ness (<c>EffectiveFeedTime &lt;= now</c>), actor-exclusion, late-joiner baseline, and 50-row cap
/// live in the query implementations. Writes only stage changes (the caller commits via IUnitOfWork).
/// </summary>
public interface IStaffNotificationRepository
{
    Task AddAsync(StaffNotification notification, CancellationToken cancellationToken = default);
    Task<StaffNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task RemoveAsync(StaffNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// The most recent due notifications for the viewer's clinic, newest first, capped at
    /// <paramref name="take"/>. Excludes notifications whose actor is the viewer (hidden entirely).
    /// </summary>
    Task<IReadOnlyList<StaffNotification>> GetRecentForUserAsync(
        Guid clinicId, string userId, DateTime nowUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Count of the viewer's unread notifications: due, not actor-excluded, effective at/after the
    /// viewer's join time (<paramref name="userCreatedAtUtc"/>), and with no read marker. Not capped
    /// at the 50-row display window.
    /// </summary>
    Task<int> CountUnreadAsync(
        Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>The viewer's currently-unread notifications (same predicate as <see cref="CountUnreadAsync"/>). Used by mark-all.</summary>
    Task<IReadOnlyList<StaffNotification>> GetUnreadForUserAsync(
        Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids of the viewer's currently-unread notifications (same predicate as <see cref="CountUnreadAsync"/>).
    /// Id-only projection for mark-all, which only needs each id to build a <see cref="NotificationRead"/>.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetUnreadIdsForUserAsync(
        Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>The subset of <paramref name="notificationIds"/> the user has already read.</summary>
    Task<IReadOnlyCollection<Guid>> GetReadNotificationIdsAsync(
        string userId, IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default);

    Task<bool> ReadMarkerExistsAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default);
    Task AddReadMarkerAsync(NotificationRead read, CancellationToken cancellationToken = default);

    /// <summary>The (single) reminder notification for an appointment, if one exists.</summary>
    Task<StaffNotification?> GetReminderByAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>The (single) post-visit review notification for an appointment, if one exists.</summary>
    Task<StaffNotification?> GetPostVisitReviewByAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The (single) live approaching-expiry alert for a stock item, if one exists (AC-P4.6). Looked up by
    /// item rather than by clinic so the daily scan can keep exactly one row per item in step with its
    /// earliest expiring batch instead of writing a fresh row on every run.
    /// </summary>
    Task<StaffNotification?> GetStockExpiringSoonByItemAsync(Guid stockItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clinic's live backup-staleness alert, if it has one (L4d). Keyed on the <b>clinic</b> and not on any
    /// id, because there is exactly one such fact per clinic — which is what makes the ensure/clear pair
    /// idempotent without a target row to hang off.
    /// </summary>
    Task<StaffNotification?> GetBackupStaleAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The cabinet's warning row for one expiry threshold, if it has one (<c>clinic-subscription</c> FR-5).
    /// Keyed on <b>(clinic, threshold)</b> and not on the clinic alone, which is the whole difference from
    /// <see cref="GetBackupStaleAsync"/>: the daily pass must be idempotent <i>within</i> a threshold while still
    /// writing a genuinely new, unread row when the next one is reached (AC-3.4, AC-3.5).
    /// </summary>
    Task<StaffNotification?> GetSubscriptionWarningAsync(
        Guid clinicId, int thresholdDays, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every subscription-expiry warning the cabinet is carrying. Read only to withdraw them all once the
    /// entitlement moves back beyond the warning window, which is what <b>re-arms</b> the thresholds so a cabinet
    /// that renews and later approaches expiry again is warned again (FR-5).
    /// </summary>
    Task<IReadOnlyList<StaffNotification>> GetSubscriptionWarningsAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The viewer's due, unread post-visit review notifications (same unread predicate as
    /// <see cref="CountUnreadAsync"/>, restricted to the <c>PostVisitReview</c> category). Drives the popup.
    /// </summary>
    Task<IReadOnlyList<StaffNotification>> GetPendingReviewsForUserAsync(
        Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);
}
