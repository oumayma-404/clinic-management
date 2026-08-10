using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// EF implementation of <see cref="IPlatformAccountRepository"/>.
///
/// <para><b>No <c>IgnoreQueryFilters()</c> anywhere, and none is needed</b> — <see cref="PlatformAccount"/>
/// carries no <c>ClinicId</c>, so no global filter is configured for it. That is the same position
/// <c>ClinicSignupRepository</c> is in, and for the same structural reason.</para>
/// </summary>
public class PlatformAccountRepository : IPlatformAccountRepository
{
    private readonly ApplicationDbContext _context;

    public PlatformAccountRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// ⚠️ Normalises here rather than trusting the caller: the unique index is on the lowered form, so a caller
    /// that passed a raw address would find nothing and then create the duplicate the index refuses.
    /// </summary>
    public Task<PlatformAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalised = EmailNormalization.Normalize(email ?? string.Empty);

        return _context.PlatformAccounts
            .Include(a => a.RecoveryCodes)
            .FirstOrDefaultAsync(a => a.Email == normalised, cancellationToken);
    }

    public Task<PlatformAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.PlatformAccounts
            .Include(a => a.RecoveryCodes)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    /// <summary>Without the child collection — this runs on <b>every</b> console request. See the interface.</summary>
    public Task<PlatformAccount?> GetForStateCheckAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.PlatformAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task AddAsync(PlatformAccount account, CancellationToken cancellationToken = default) =>
        await _context.PlatformAccounts.AddAsync(account, cancellationToken);
}
