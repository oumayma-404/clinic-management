using Microsoft.EntityFrameworkCore;
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

    public async Task<IEnumerable<Notification>> GetPendingNotificationsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .Where(n => n.Status == NotificationStatus.Pending && n.ScheduledFor <= DateTime.UtcNow)
            .OrderBy(n => n.ScheduledFor)
            .ToListAsync(cancellationToken);
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
            .Where(n => n.ClinicId == clinicId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
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



