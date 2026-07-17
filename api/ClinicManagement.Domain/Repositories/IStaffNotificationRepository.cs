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
    /// The viewer's due, unread post-visit review notifications (same unread predicate as
    /// <see cref="CountUnreadAsync"/>, restricted to the <c>PostVisitReview</c> category). Drives the popup.
    /// </summary>
    Task<IReadOnlyList<StaffNotification>> GetPendingReviewsForUserAsync(
        Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);
}
