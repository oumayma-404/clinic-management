using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

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

    public async Task<IEnumerable<DentalActCode>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.DentalActCodes.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(e => e.IsActive);
        }

        return await query
            .OrderBy(e => e.Category)
            .ThenBy(e => e.CodeActe)
            .ToListAsync(cancellationToken);
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
