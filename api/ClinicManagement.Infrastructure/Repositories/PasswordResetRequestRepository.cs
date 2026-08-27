using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// No <c>IgnoreQueryFilters()</c> anywhere here, and none is needed: <see cref="PasswordResetRequest"/> has no
/// <c>ClinicId</c>, so no filter is configured for it in the first place.
/// </summary>
public class PasswordResetRequestRepository : IPasswordResetRequestRepository
{
    /// <summary>Rows trimmed per request. Bounded so one anonymous call can never pay for a large backlog.</summary>
    private const int PurgeBatchSize = 200;

    private readonly ApplicationDbContext _context;

    public PasswordResetRequestRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PasswordResetRequest?> GetByUserIdAsync(
        string userId, CancellationToken cancellationToken = default) =>
        await _context.PasswordResetRequests
            .FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);

    public async Task<PasswordResetRequest?> GetByTokenHashAsync(
        string tokenHash, CancellationToken cancellationToken = default) =>
        await _context.PasswordResetRequests
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(PasswordResetRequest request, CancellationToken cancellationToken = default) =>
        await _context.PasswordResetRequests.AddAsync(request, cancellationToken);

    public Task UpdateAsync(PasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        // The guarded form ClinicSignupRepository and PatientRepository use: Version is mapped onto xmin, so a
        // blind Update on a detached instance sends `WHERE xmin = 0`, matches nothing, and 409s with nobody at
        // fault.
        if (_context.Entry(request).State == EntityState.Detached)
        {
            _context.PasswordResetRequests.Update(request);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes spent rows outright, bounded per call, and <b>does not ride the caller's transaction</b> — the shape
    /// <c>ClinicSignupRepository.PurgeSpentAsync</c> arrived at after staging these deletes on the caller's
    /// <c>SaveChangesAsync</c> turned a concurrent purge into a 409 on a valid request. Purging spent rows
    /// regardless of whether the accompanying request succeeds is harmless, which is what makes the independent
    /// delete the right shape.
    /// </summary>
    public async Task<int> PurgeSpentAsync(
        DateTime nowUtc, TimeSpan consumedRetention, CancellationToken cancellationToken = default)
    {
        var consumedBefore = nowUtc - consumedRetention;

        var ids = await _context.PasswordResetRequests
            .Where(r => (r.ConsumedAtUtc == null && r.ExpiresAtUtc <= nowUtc)
                        || (r.ConsumedAtUtc != null && r.ConsumedAtUtc <= consumedBefore))
            .OrderBy(r => r.ExpiresAtUtc)
            .Select(r => r.Id)
            .Take(PurgeBatchSize)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return 0;
        }

        return await _context.PasswordResetRequests
            .Where(r => ids.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
