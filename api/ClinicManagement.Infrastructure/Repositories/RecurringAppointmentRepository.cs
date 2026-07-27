using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class RecurringAppointmentRepository : IRecurringAppointmentRepository
{
    private readonly ApplicationDbContext _context;

    public RecurringAppointmentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RecurringAppointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.RecurringAppointments
            .Include(r => r.Patient)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<RecurringAppointment>> GetByClinicIdAsync(Guid clinicId, bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = _context.RecurringAppointments
            .Include(r => r.Patient)
            .Where(r => r.ClinicId == clinicId);

        if (activeOnly)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<RecurringAppointment> AddAsync(RecurringAppointment series, CancellationToken cancellationToken = default)
    {
        await _context.RecurringAppointments.AddAsync(series, cancellationToken);
        return series;
    }

    public Task UpdateAsync(RecurringAppointment series, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(series);
        if (entry.State == EntityState.Detached)
        {
            _context.RecurringAppointments.Update(series);
        }
        return Task.CompletedTask;
    }
}
