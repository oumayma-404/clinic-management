using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// The archive device grants over EF Core (<c>clinic-archive-auto-copy</c>), on
/// <see cref="ClinicRecoveryPointRepository"/>'s shape.
/// </summary>
public class ClinicArchiveGrantRepository : IClinicArchiveGrantRepository
{
    private readonly ApplicationDbContext _context;

    public ClinicArchiveGrantRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ClinicArchiveGrant>> ListAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Ends on Id for ClinicRecoveryPointRepository's reason: two grants issued in the same tick would
        // otherwise reshuffle between renders.
        return await _context.ClinicArchiveGrants
            .Where(g => g.ClinicId == clinicId)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ThenByDescending(g => g.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<ClinicArchiveGrant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ClinicArchiveGrants.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
    }

    /// <summary>
    /// ⚠️ <b><c>IgnoreQueryFilters()</c>, and it is the one place in this class that needs it.</b> A shell presenting
    /// a grant has no session, so nothing has put a clinic in scope and the filter would match zero rows for every
    /// secret — the grant would simply never work. The row's own <c>ClinicId</c> is what the caller then compares
    /// against the cabinet it is about to serve, which is a stronger check than the ambient one because it is
    /// explicit and testable (AC-4).
    /// </summary>
    public async Task<ClinicArchiveGrant?> FindBySecretHashAsync(
        string secretHash, CancellationToken cancellationToken = default)
    {
        return await _context.ClinicArchiveGrants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.SecretHash == secretHash, cancellationToken);
    }

    public async Task AddAsync(ClinicArchiveGrant grant, CancellationToken cancellationToken = default)
    {
        await _context.ClinicArchiveGrants.AddAsync(grant, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ClinicArchiveGrant grant, CancellationToken cancellationToken = default)
    {
        _context.ClinicArchiveGrants.Update(grant);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
