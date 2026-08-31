using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICnamCatalogRepository"/>. <c>CnamLetterValues</c> is per-clinic and
/// carries a <c>HasQueryFilter</c> in <see cref="ApplicationDbContext"/>. Mutations only stage changes; the
/// UnitOfWork commits.
/// </summary>
public class CnamCatalogRepository : ICnamCatalogRepository
{
    private readonly ApplicationDbContext _context;

    public CnamCatalogRepository(ApplicationDbContext context)
    {
        _context = context;
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
