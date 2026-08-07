using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICnamCatalogRepository"/>. The two tables are global (no
/// <c>HasQueryFilter</c> in <see cref="ApplicationDbContext"/>), so every clinic sees the same rows.
/// Mutations only stage changes; the UnitOfWork commits.
/// </summary>
public class CnamCatalogRepository : ICnamCatalogRepository
{
    private readonly ApplicationDbContext _context;

    public CnamCatalogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    // ── Nomenclature entries ────────────────────────────────────────────────────────────────────

    public async Task<CnamNomenclatureEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CnamNomenclatureEntries
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<PagedResult<CnamNomenclatureEntry>> GetAllAsync(
        bool includeInactive = false,
        string? category = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CnamNomenclatureEntries.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(e => e.IsActive);
        }

        // Category and the free-text term used to be applied in the handler, over the DTOs, after the whole
        // catalog had been read. Both are here now: a page cut in SQL and then filtered in memory is not the
        // page anyone asked for, and the search would only ever have seen the rows already on it.
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
                EF.Functions.ILike(SqlSearch.Unaccent(e.DesignationFr)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(e.LettreCle)!, pattern, SqlSearch.EscapeString));
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
        var query = _context.CnamNomenclatureEntries
            .Where(e => e.CodeActe.ToLower() == normalized.ToLower());

        if (excludeId.HasValue)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<CnamNomenclatureEntry> AddAsync(CnamNomenclatureEntry entry, CancellationToken cancellationToken = default)
    {
        await _context.CnamNomenclatureEntries.AddAsync(entry, cancellationToken);
        return entry;
    }

    public Task UpdateAsync(CnamNomenclatureEntry entry, CancellationToken cancellationToken = default)
    {
        if (_context.Entry(entry).State == EntityState.Detached)
        {
            _context.CnamNomenclatureEntries.Update(entry);
        }
        return Task.CompletedTask;
    }

    // ── Valeurs de la lettre clé (VLC) ──────────────────────────────────────────────────────────

    public async Task<IEnumerable<CnamLetterValue>> GetAllLetterValuesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CnamLetterValues
            .OrderBy(v => v.LettreCle)
            .ToListAsync(cancellationToken);
    }

    public async Task<CnamLetterValue?> GetLetterValueByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CnamLetterValues
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<CnamLetterValue?> GetLetterValueByCleAsync(string lettreCle, CancellationToken cancellationToken = default)
    {
        var normalized = lettreCle.Trim().ToUpper();
        return await _context.CnamLetterValues
            .FirstOrDefaultAsync(v => v.LettreCle.ToUpper() == normalized, cancellationToken);
    }

    public Task UpdateLetterValueAsync(CnamLetterValue value, CancellationToken cancellationToken = default)
    {
        if (_context.Entry(value).State == EntityState.Detached)
        {
            _context.CnamLetterValues.Update(value);
        }
        return Task.CompletedTask;
    }
}
