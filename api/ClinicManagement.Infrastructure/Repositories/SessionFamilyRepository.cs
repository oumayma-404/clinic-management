using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// Session families (<c>hosted-security-hardening</c> FR-1.6).
///
/// <para><b>No <c>IgnoreQueryFilters()</c> anywhere, and none is needed</b>: <see cref="SessionFamily"/> carries
/// no <c>ClinicId</c>, so no filter is configured for it — see the entity's own note for why that absence is
/// deliberate rather than an omission.</para>
/// </summary>
public class SessionFamilyRepository : ISessionFamilyRepository
{
    private readonly ApplicationDbContext _context;

    public SessionFamilyRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// ⚠️ Matches the current hash <b>or</b> the immediate predecessor, and deliberately does <b>not</b> filter on
    /// « still live »: an ended family must still be found, or a replayed credential would be indistinguishable
    /// from one that was never ours — and that distinction is the entire signal.
    /// </summary>
    public async Task<SessionFamily?> GetByCredentialAsync(
        string credentialHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(credentialHash))
        {
            return null;
        }

        return await _context.SessionFamilies
            .FirstOrDefaultAsync(
                f => f.CurrentCredentialHash == credentialHash || f.PreviousCredentialHash == credentialHash,
                cancellationToken);
    }

    public async Task<IReadOnlyList<SessionFamily>> GetLiveForUserAsync(
        string userId, CancellationToken cancellationToken = default) =>
        await _context.SessionFamilies
            .Where(f => f.UserId == userId && f.EndedAtUtc == null)
            .OrderByDescending(f => f.LastRotatedAt)
            .ThenBy(f => f.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SessionFamily family, CancellationToken cancellationToken = default) =>
        await _context.SessionFamilies.AddAsync(family, cancellationToken);

    /// <summary>
    /// ⚠️ <b>Expiry only — never « old ».</b> A live family is live because a device is still using it, so
    /// pruning by age would sign working users out on a schedule. An <i>ended</i> row is kept until its own
    /// credential lifetime runs out too, so the replay it recorded stays visible for as long as the credential
    /// that caused it could still be presented.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await _context.SessionFamilies
            .Where(f => f.ExpiresAtUtc < nowUtc)
            .ExecuteDeleteAsync(cancellationToken);
}
