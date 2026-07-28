using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class ProcedureTypeRepository : IProcedureTypeRepository
{
    private readonly ApplicationDbContext _context;

    public ProcedureTypeRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProcedureType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ProcedureTypes
            // The material list is part of the aggregate (AC-P4.9). Without this the consumption service reads
            // an empty list and silently consumes nothing — the failure mode would look exactly like the
            // opt-out case (AC-P4.11), which is the one thing it must not be confusable with.
            .Include(pt => pt.Materials)
            .FirstOrDefaultAsync(pt => pt.Id == id, cancellationToken);
    }

    public async Task<ProcedureType?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.ProcedureTypes
            .FirstOrDefaultAsync(pt => pt.Name.ToLower() == name.ToLower(), cancellationToken);
    }

    public async Task<IEnumerable<ProcedureType>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ProcedureTypes
            .OrderBy(pt => pt.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ProcedureType>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ProcedureTypes
            .Where(pt => pt.IsActive)
            .OrderBy(pt => pt.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ProcedureTypes
            .Where(pt => pt.Name.ToLower() == name.ToLower());

        if (excludeId.HasValue)
        {
            query = query.Where(pt => pt.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<ProcedureType> AddAsync(ProcedureType procedureType, CancellationToken cancellationToken = default)
    {
        await _context.ProcedureTypes.AddAsync(procedureType, cancellationToken);
        return procedureType;
    }

    public Task UpdateAsync(ProcedureType procedureType, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(procedureType);
        if (entry.State == EntityState.Detached)
        {
            _context.ProcedureTypes.Update(procedureType);
        }
        else
        {
            // Entity is already tracked - mark all modified properties
            // EF Core should detect changes automatically, but we'll ensure DefaultCost is tracked
            // Check if DefaultCost has actually changed
            var defaultCostProperty = entry.Property(pt => pt.DefaultCost);
            if (defaultCostProperty.IsModified || defaultCostProperty.CurrentValue != defaultCostProperty.OriginalValue)
            {
                defaultCostProperty.IsModified = true;
            }
            
            // Always mark UpdatedAt as modified
            entry.Property(pt => pt.UpdatedAt).IsModified = true;
        }
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var procedureType = await GetByIdAsync(id, cancellationToken);
        if (procedureType != null)
        {
            _context.ProcedureTypes.Remove(procedureType);
        }
    }
}


