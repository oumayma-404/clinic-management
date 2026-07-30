using Microsoft.EntityFrameworkCore;
using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
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

    // The list reads Include the material list for the same reason GetByIdAsync does (AC-P4.14): the catalog
    // screen shows which acts consume stock, and a silently-empty list is indistinguishable from an act that
    // has opted out (AC-P4.11). This is a small per-clinic catalog, so the extra join is not a concern.
    /// <summary>
    /// The single list read for the act catalog. It replaced <c>GetAllAsync</c> + <c>GetActiveAsync</c>, which
    /// differed only by one <c>Where</c> and would have needed the same paging and the same search predicate
    /// added to both — two copies of a list read is how they drift.
    /// </summary>
    public async Task<PagedResult<ProcedureType>> GetFilteredAsync(
        Guid clinicId,
        bool includeInactive = false,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        // The clinic predicate is explicit and in SQL. The handler used to apply it in memory AFTER the read,
        // for defense-in-depth over the fail-open global filter — harmless when the read returned everything,
        // but with a page it would filter rows out of an already-cut window and shrink pages unpredictably.
        var query = _context.ProcedureTypes
            .Include(pt => pt.Materials)
            .Where(pt => pt.ClinicId == clinicId);

        if (!includeInactive)
        {
            query = query.Where(pt => pt.IsActive);
        }

        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(pt =>
                EF.Functions.ILike(SqlSearch.Unaccent(pt.Name)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(pt.Description)!, pattern, SqlSearch.EscapeString));
        }

        return await query
            .OrderBy(pt => pt.Name)
            .ThenBy(pt => pt.Id)
            .ToPagedResultAsync(paging, cancellationToken);
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


