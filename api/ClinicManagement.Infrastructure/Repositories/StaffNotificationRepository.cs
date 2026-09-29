using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class StaffNotificationRepository : IStaffNotificationRepository
{
    private readonly ApplicationDbContext _context;

    public StaffNotificationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(StaffNotification notification, CancellationToken cancellationToken = default)
    {
        await _context.StaffNotifications.AddAsync(notification, cancellationToken);
    }

    public async Task<StaffNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.StaffNotifications
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public Task RemoveAsync(StaffNotification notification, CancellationToken cancellationToken = default)
    {
        _context.StaffNotifications.Remove(notification);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<StaffNotification>> GetRecentForUserAsync(
        Guid clinicId, string userId, DateTime nowUtc, int take, CancellationToken cancellationToken = default)
    {
        return await VisibleQuery(clinicId, userId, nowUtc)
            .OrderByDescending(n => n.EffectiveFeedTime)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> GetVisibleIdsForUserAsync(
        Guid clinicId, string userId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        return await VisibleQuery(clinicId, userId, nowUtc)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The single definition of « what this viewer's bell shows »: due, in this clinic, NOT actor-excluded (the
    /// viewer never sees their own action's notification), targeted at everyone or at this viewer, and not
    /// dismissed by them.
    ///
    /// <para>⚠️ Extracted so the list and « Tout effacer » cannot disagree about what is on screen. A second
    /// hand-written copy of this predicate is how « effacer tout » leaves rows behind — the failure is silent,
    /// since both halves look right in isolation.</para>
    /// </summary>
    private IQueryable<StaffNotification> VisibleQuery(Guid clinicId, string userId, DateTime nowUtc)
    {
        return _context.StaffNotifications
            .Where(n => n.ClinicId == clinicId
                        && n.EffectiveFeedTime <= nowUtc
                        && (n.ActorUserId == null || n.ActorUserId != userId)
                        && (n.TargetUserId == null || n.TargetUserId == userId)
                        && !_context.NotificationDismissals.Any(d => d.NotificationId == n.Id && d.UserId == userId))
            .Where(NotAReviewOfAVisitThatDidNotHappen());
    }

    /// <summary>
    /// Hides a post-visit review whose séance was supprimée (« créé par erreur »), annulée or marked absent.
    ///
    /// <para>⚠️ A read rule, not only a write one: « Supprimer » (<c>DisregardVisitsCommand</c>) and the series
    /// cancel never removed the review, so the popup asked for a fiche on a visit nobody sat in, every day, with
    /// no row left on « À clôturer » to answer it. Filtering here covers every writer and the rows already
    /// stranded, and « remettre dans la liste » brings the prompt back with no extra write.</para>
    ///
    /// <para>Phrased as « no such dead appointment exists » so an unscoped read fails open (shows the review)
    /// instead of hiding them all.</para>
    /// </summary>
    private System.Linq.Expressions.Expression<Func<StaffNotification, bool>> NotAReviewOfAVisitThatDidNotHappen() =>
        n => n.Category != NotificationCategory.PostVisitReview
             || n.AppointmentId == null
             || !_context.Appointments.Any(a => a.Id == n.AppointmentId
                                                && (a.DisregardedAtUtc != null
                                                    || a.Status == AppointmentStatus.Cancelled
                                                    || a.Status == AppointmentStatus.NoShow));

    public async Task<int> CountUnreadAsync(
        Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        return await UnreadQuery(clinicId, userId, userCreatedAtUtc, nowUtc)
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffNotification>> GetUnreadForUserAsync(
        Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        return await UnreadQuery(clinicId, userId, userCreatedAtUtc, nowUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> GetUnreadIdsForUserAsync(
        Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        return await UnreadQuery(clinicId, userId, userCreatedAtUtc, nowUtc)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);
    }

    // The single definition of "unread for this viewer": due, in-clinic, not actor-excluded, targeted at
    // everyone or this viewer, effective at/after the viewer's join time (late-joiner baseline), not dismissed
    // by them, and with no read marker.
    //
    // ⚠️ The dismissal clause is load-bearing rather than tidy: this predicate drives the bell's BADGE and the
    // post-visit popup's queue. Without it, clearing the bell would leave the badge counting rows the reader
    // can no longer reach — an unread count with nothing behind it — and the popup would go on prompting for a
    // review whose row the same person had just cleared.
    private IQueryable<StaffNotification> UnreadQuery(Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc)
    {
        return _context.StaffNotifications
            .Where(n => n.ClinicId == clinicId
                        && n.EffectiveFeedTime <= nowUtc
                        && n.EffectiveFeedTime >= userCreatedAtUtc
                        && (n.ActorUserId == null || n.ActorUserId != userId)
                        && (n.TargetUserId == null || n.TargetUserId == userId)
                        && !_context.NotificationDismissals.Any(d => d.NotificationId == n.Id && d.UserId == userId)
                        && !_context.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == userId))
            .Where(NotAReviewOfAVisitThatDidNotHappen());
    }

    public async Task<IReadOnlyCollection<Guid>> GetReadNotificationIdsAsync(
        string userId, IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken = default)
    {
        if (notificationIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        return await _context.NotificationReads
            .Where(r => r.UserId == userId && notificationIds.Contains(r.NotificationId))
            .Select(r => r.NotificationId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ReadMarkerExistsAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default)
    {
        return await _context.NotificationReads
            .AnyAsync(r => r.NotificationId == notificationId && r.UserId == userId, cancellationToken);
    }

    public async Task AddReadMarkerAsync(NotificationRead read, CancellationToken cancellationToken = default)
    {
        await _context.NotificationReads.AddAsync(read, cancellationToken);
    }

    public async Task<bool> DismissalExistsAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default)
    {
        return await _context.NotificationDismissals
            .AnyAsync(d => d.NotificationId == notificationId && d.UserId == userId, cancellationToken);
    }

    public async Task AddDismissalAsync(NotificationDismissal dismissal, CancellationToken cancellationToken = default)
    {
        await _context.NotificationDismissals.AddAsync(dismissal, cancellationToken);
    }

    public async Task<StaffNotification?> GetReminderByAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffNotifications
            .FirstOrDefaultAsync(
                n => n.AppointmentId == appointmentId && n.Category == NotificationCategory.Reminder,
                cancellationToken);
    }

    public async Task<StaffNotification?> GetPostVisitReviewByAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffNotifications
            .FirstOrDefaultAsync(
                n => n.AppointmentId == appointmentId && n.Category == NotificationCategory.PostVisitReview,
                cancellationToken);
    }

    public async Task<StaffNotification?> GetStockExpiringSoonByItemAsync(Guid stockItemId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffNotifications
            .FirstOrDefaultAsync(
                n => n.StockItemId == stockItemId && n.Category == NotificationCategory.StockExpiringSoon,
                cancellationToken);
    }

    public async Task<StaffNotification?> GetArchiveStaleAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters for GetBackupStaleAsync's reason below: the daily pass runs UseSystemWide, while the
        // clear fires inside an admin's own request after a delivered download. The clinicId parameter is the
        // authoritative check either way.
        return await _context.StaffNotifications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                n => n.ClinicId == clinicId && n.Category == NotificationCategory.ArchiveStale,
                cancellationToken);
    }

    public async Task<StaffNotification?> GetVaultCopyStaleAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters for GetArchiveStaleAsync's reason: the daily pass runs UseSystemWide, while the clear
        // fires inside the shell's own reporting request. The clinicId parameter is the authoritative check.
        return await _context.StaffNotifications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                n => n.ClinicId == clinicId && n.Category == NotificationCategory.VaultCopyStale,
                cancellationToken);
    }

    public async Task<StaffNotification?> GetBackupStaleAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        // ⚠️ IgnoreQueryFilters: the daily BackupJob runs with no clinic in scope, so the filter is inactive
        // there and this reads fine — but the *manual* backup path runs inside an admin's request, where the
        // filter is active and scoped to that same clinic. Explicit is better than relying on which caller it is:
        // the clinicId parameter is the authoritative check either way, exactly as everywhere else here.
        return await _context.StaffNotifications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                n => n.ClinicId == clinicId && n.Category == NotificationCategory.BackupStale,
                cancellationToken);
    }

    public async Task<StaffNotification?> GetSubscriptionWarningAsync(
        Guid clinicId, int thresholdDays, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters for GetBackupStaleAsync's reason: the daily job runs UseSystemWide, but a vendor
        // command clearing a warning after a grant runs scoped to one cabinet. The clinicId parameter is the
        // authoritative check either way, exactly as everywhere else here.
        return await _context.StaffNotifications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                n => n.ClinicId == clinicId
                     && n.Category == NotificationCategory.SubscriptionExpiring
                     && n.SubscriptionThresholdDays == thresholdDays,
                cancellationToken);
    }

    public async Task<IReadOnlyList<StaffNotification>> GetSubscriptionWarningsAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffNotifications
            .IgnoreQueryFilters()
            .Where(n => n.ClinicId == clinicId && n.Category == NotificationCategory.SubscriptionExpiring)
            .ToListAsync(cancellationToken);
    }

    public async Task<StaffNotification?> GetMessagingWarningAsync(
        Guid clinicId, string monthKey, int thresholdPercent, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters for its two siblings' reason: the post-commit hook runs inside the dispatcher's
        // UseSystemWide scope while the daily pass runs per cabinet. The clinicId parameter is the authoritative
        // check either way.
        return await _context.StaffNotifications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                n => n.ClinicId == clinicId
                     && n.Category == NotificationCategory.MessagingAllowanceLow
                     && n.MessagingAllowanceMonth == monthKey
                     && n.MessagingThresholdPercent == thresholdPercent,
                cancellationToken);
    }

    public async Task<IReadOnlyList<StaffNotification>> GetMessagingWarningsAsync(
        Guid clinicId, string? monthKey = null, CancellationToken cancellationToken = default)
    {
        var query = _context.StaffNotifications
            .IgnoreQueryFilters()
            .Where(n => n.ClinicId == clinicId && n.Category == NotificationCategory.MessagingAllowanceLow);

        if (monthKey != null)
        {
            query = query.Where(n => n.MessagingAllowanceMonth == monthKey);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffNotification>> GetPendingReviewsForUserAsync(
        Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        return await UnreadQuery(clinicId, userId, userCreatedAtUtc, nowUtc)
            .Where(n => n.Category == NotificationCategory.PostVisitReview)
            .OrderByDescending(n => n.EffectiveFeedTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffNotification>> GetByPatientAsync(
        Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffNotifications
            .Where(n => n.PatientId == patientId)
            .ToListAsync(cancellationToken);
    }
}
