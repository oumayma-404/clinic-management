using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class LabWorkOrderRepository : ILabWorkOrderRepository
{
    private readonly ApplicationDbContext _context;

    public LabWorkOrderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<LabWorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.LabWorkOrders
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<LabWorkOrder>> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.LabWorkOrders
            .Include(o => o.Patient)
            .Where(o => o.ClinicId == clinicId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<LabWorkOrder>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.LabWorkOrders
            .Include(o => o.Patient)
            .Where(o => o.PatientId == patientId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<LabWorkOrder> AddAsync(LabWorkOrder order, CancellationToken cancellationToken = default)
    {
        await _context.LabWorkOrders.AddAsync(order, cancellationToken);
        return order;
    }

    public Task UpdateAsync(LabWorkOrder order, CancellationToken cancellationToken = default)
    {
        _context.LabWorkOrders.Update(order);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await GetByIdAsync(id, cancellationToken);
        if (order != null)
        {
            _context.LabWorkOrders.Remove(order);
        }
    }
}
