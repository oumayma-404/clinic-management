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
        // Due, in this clinic, and NOT actor-excluded (the viewer never sees their own action's
        // notification). Newest first, capped. Null-actor rows are visible to everyone.
        return await _context.StaffNotifications
            .Where(n => n.ClinicId == clinicId
                        && n.EffectiveFeedTime <= nowUtc
                        && (n.ActorUserId == null || n.ActorUserId != userId))
            .OrderByDescending(n => n.EffectiveFeedTime)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

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

    // The single definition of "unread for this viewer": due, in-clinic, not actor-excluded, effective
    // at/after the viewer's join time (late-joiner baseline), and with no read marker.
    private IQueryable<StaffNotification> UnreadQuery(Guid clinicId, string userId, DateTime userCreatedAtUtc, DateTime nowUtc)
    {
        return _context.StaffNotifications
            .Where(n => n.ClinicId == clinicId
                        && n.EffectiveFeedTime <= nowUtc
                        && n.EffectiveFeedTime >= userCreatedAtUtc
                        && (n.ActorUserId == null || n.ActorUserId != userId)
                        && !_context.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == userId));
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

    public async Task<StaffNotification?> GetReminderByAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffNotifications
            .FirstOrDefaultAsync(
                n => n.AppointmentId == appointmentId && n.Category == NotificationCategory.Reminder,
                cancellationToken);
    }
}
