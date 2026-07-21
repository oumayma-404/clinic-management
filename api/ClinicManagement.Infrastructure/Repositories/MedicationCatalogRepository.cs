using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IMedicationCatalogRepository"/>. The tables are global (no
/// <c>HasQueryFilter</c> in <see cref="ApplicationDbContext"/>), so every clinic sees the same catalog.
/// Mutations only stage changes; the UnitOfWork commits.
/// </summary>
public class MedicationCatalogRepository : IMedicationCatalogRepository
{
    private readonly ApplicationDbContext _context;

    public MedicationCatalogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Medication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Medications
            .Include(m => m.ActiveIngredients)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Medication>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Medications
            .Include(m => m.ActiveIngredients)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        return await query
            .OrderBy(m => m.BrandName)
            .ThenBy(m => m.Strength)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> BrandExistsAsync(string brandName, string form, string strength, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var b = (brandName ?? string.Empty).Trim().ToLower();
        var f = (form ?? string.Empty).Trim().ToLower();
        var s = (strength ?? string.Empty).Trim().ToLower();

        var query = _context.Medications.Where(m =>
            m.BrandName.ToLower() == b &&
            m.Form.ToLower() == f &&
            m.Strength.ToLower() == s);

        if (excludeId.HasValue)
        {
            query = query.Where(m => m.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<Medication> AddAsync(Medication medication, CancellationToken cancellationToken = default)
    {
        await _context.Medications.AddAsync(medication, cancellationToken);
        return medication;
    }

    public Task UpdateAsync(Medication medication, CancellationToken cancellationToken = default)
    {
        if (_context.Entry(medication).State == EntityState.Detached)
        {
            _context.Medications.Update(medication);
        }
        return Task.CompletedTask;
    }
}
