using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly ApplicationDbContext _context;

    public NotificationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Notification>> GetPendingNotificationsAsync(
        int take, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            // Served by IX_Notifications_Status_ScheduledFor (AC-P4.30): the predicate and the ORDER BY both
            // come off the same index, which this query ran without until now.
            .Where(n => n.Status == NotificationStatus.Pending && n.ScheduledFor <= DateTime.UtcNow)
            .OrderBy(n => n.ScheduledFor)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> PurgeTerminalOlderThanAsync(
        DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        // Terminal statuses ONLY (AC-P4.34). A Pending row is never in scope no matter how old: an unsent
        // reminder is voided deliberately by VoidUnsentAsync, and deleting one here would leave a patient
        // un-contacted with nothing recording why.
        return await _context.Notifications
            .Where(n => (n.Status == NotificationStatus.Sent || n.Status == NotificationStatus.Failed)
                        && n.CreatedAt < olderThanUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IEnumerable<Notification>> GetByAppointmentIdAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .Where(n => n.AppointmentId == appointmentId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Notification>> GetRecentByClinicIdAsync(Guid clinicId, int take, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .Include(n => n.Patient)
            // The appointment too (AC-P3.9): the status row has to say which visit a failed reminder was for,
            // not just that one failed.
            .Include(n => n.Appointment)
            .Where(n => n.ClinicId == clinicId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<Notification>> GetClinicLogAsync(
        Guid clinicId,
        NotificationStatus? status,
        NotificationType? channel,
        DateTime? fromUtc,
        DateTime? toUtcInclusive,
        PageRequest? paging,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Notifications
            .Include(n => n.Patient)
            // The appointment too (AC-P3.9): the row has to say which visit a failed reminder was for.
            .Include(n => n.Appointment)
            .Where(n => n.ClinicId == clinicId);

        // Each filter is applied only when supplied — a null means "not applied", never "match nothing".
        if (status.HasValue)
        {
            query = query.Where(n => n.Status == status.Value);
        }

        if (channel.HasValue)
        {
            query = query.Where(n => n.Type == channel.Value);
        }

        // The window is on ScheduledFor, not CreatedAt: « les rappels de la semaine dernière » means the ones due
        // then, and a row created weeks earlier for a lead-time tier would fall outside a CreatedAt window.
        if (fromUtc.HasValue)
        {
            query = query.Where(n => n.ScheduledFor >= fromUtc.Value);
        }

        if (toUtcInclusive.HasValue)
        {
            query = query.Where(n => n.ScheduledFor <= toUtcInclusive.Value);
        }

        return await query
            .OrderByDescending(n => n.ScheduledFor)
            // `.ThenBy(Id)` is not decoration: OFFSET over a non-unique sort can show one row on two pages and
            // skip another, which reads as "a message vanished from the log".
            .ThenBy(n => n.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<ReminderLogCounts> GetClinicLogCountsAsync(
        Guid clinicId,
        DateTime todayFromUtc,
        DateTime todayToUtcInclusive,
        DateTime failedSinceUtc,
        CancellationToken cancellationToken = default)
    {
        // One GROUP BY rather than three round trips whose bounds could drift apart from each other.
        var scoped = _context.Notifications.Where(n => n.ClinicId == clinicId);

        var sentToday = await scoped.CountAsync(
            n => n.Status == NotificationStatus.Sent
                 && n.SentAt >= todayFromUtc
                 && n.SentAt <= todayToUtcInclusive,
            cancellationToken);

        // Pending is deliberately unbounded by date: a backlog is a backlog whenever it was queued, and a
        // pending row from yesterday is the one most worth noticing.
        var pending = await scoped.CountAsync(n => n.Status == NotificationStatus.Pending, cancellationToken);

        var failedRecent = await scoped.CountAsync(
            n => n.Status == NotificationStatus.Failed && n.ScheduledFor >= failedSinceUtc,
            cancellationToken);

        return new ReminderLogCounts(sentToday, pending, failedRecent);
    }

    public async Task<IEnumerable<Notification>> GetRecallBatchAsync(
        Guid patientId, DateTime scheduledFor, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .Where(n => n.PatientId == patientId
                     && n.AppointmentId == null
                     && n.ScheduledFor == scheduledFor)
            .ToListAsync(cancellationToken);
    }

    public async Task<Notification> AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await _context.Notifications.AddAsync(notification, cancellationToken);
        return notification;
    }

    public Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(notification);
        if (entry.State == EntityState.Detached)
        {
            _context.Notifications.Update(notification);
        }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        _context.Notifications.Remove(notification);
        return Task.CompletedTask;
    }
}



