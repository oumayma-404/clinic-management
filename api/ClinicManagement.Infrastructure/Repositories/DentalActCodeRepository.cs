using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDentalActCodeRepository"/>. The table is global (no
/// <c>HasQueryFilter</c> in <see cref="ApplicationDbContext"/>), so every clinic sees the same rows.
/// Mutations only stage changes; the UnitOfWork commits.
/// </summary>
public class DentalActCodeRepository : IDentalActCodeRepository
{
    private readonly ApplicationDbContext _context;

    public DentalActCodeRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DentalActCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.DentalActCodes
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<PagedResult<DentalActCode>> GetAllAsync(
        bool includeInactive = false,
        string? category = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DentalActCodes.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(e => e.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var trimmed = category.Trim();
            query = query.Where(e => e.Category.ToLower() == trimmed.ToLower());
        }

        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(e =>
                EF.Functions.ILike(SqlSearch.Unaccent(e.CodeActe)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(e.DesignationFr)!, pattern, SqlSearch.EscapeString));
        }

        return await query
            .OrderBy(e => e.Category)
            .ThenBy(e => e.CodeActe)
            .ThenBy(e => e.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<bool> CodeActeExistsAsync(string codeActe, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = codeActe.Trim();
        var query = _context.DentalActCodes
            .Where(e => e.CodeActe.ToLower() == normalized.ToLower());

        if (excludeId.HasValue)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> AnyProvisionalAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DentalActCodes.AnyAsync(e => e.IsProvisional, cancellationToken);
    }

    public async Task<IEnumerable<DentalActCode>> GetProvisionalAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DentalActCodes
            .Where(e => e.IsProvisional)
            .ToListAsync(cancellationToken);
    }

    public async Task<DentalActCode> AddAsync(DentalActCode entry, CancellationToken cancellationToken = default)
    {
        await _context.DentalActCodes.AddAsync(entry, cancellationToken);
        return entry;
    }

    public Task UpdateAsync(DentalActCode entry, CancellationToken cancellationToken = default)
    {
        if (_context.Entry(entry).State == EntityState.Detached)
        {
            _context.DentalActCodes.Update(entry);
        }
        return Task.CompletedTask;
    }
}
