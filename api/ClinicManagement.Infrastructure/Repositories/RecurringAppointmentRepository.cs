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
        _context.RecurringAppointments.Update(series);
        return Task.CompletedTask;
    }
}
