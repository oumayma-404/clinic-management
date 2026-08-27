using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
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

    public async Task<PagedResult<Medication>> GetAllAsync(
        bool includeInactive = false,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Medications
            .Include(m => m.ActiveIngredients)
            .AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        // The DCI clause is why this predicate belongs in SQL rather than over the mapped DTOs: prescribers
        // search by molecule at least as often as by brand ("amoxicilline", not "Clamoxyl"), and that is an
        // EXISTS over the child table — reachable from a page of parents only by having read them all first.
        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(m =>
                EF.Functions.ILike(SqlSearch.Unaccent(m.BrandName)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(m.Form)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(m.Strength)!, pattern, SqlSearch.EscapeString) ||
                m.ActiveIngredients.Any(i =>
                    EF.Functions.ILike(SqlSearch.Unaccent(i.Dci)!, pattern, SqlSearch.EscapeString)));
        }

        return await query
            .OrderBy(m => m.BrandName)
            .ThenBy(m => m.Strength)
            .ThenBy(m => m.Id)
            .ToPagedResultAsync(paging, cancellationToken);
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
