using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// No <c>IgnoreQueryFilters()</c> anywhere here, and none is needed: <see cref="ClinicSignup"/> has no
/// <c>ClinicId</c>, so no filter is configured for it in the first place.
/// </summary>
public class ClinicSignupRepository : IClinicSignupRepository
{
    /// <summary>Rows trimmed per signup. Bounded so one anonymous request can never pay for a large backlog.</summary>
    private const int PurgeBatchSize = 200;

    private readonly ApplicationDbContext _context;

    public ClinicSignupRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ClinicSignup?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = ClinicSignup.NormalizeEmail(email);
        return await _context.ClinicSignups
            .FirstOrDefaultAsync(s => s.Email == normalized, cancellationToken);
    }

    public async Task<ClinicSignup?> GetByTokenHashAsync(
        string tokenHash, CancellationToken cancellationToken = default) =>
        await _context.ClinicSignups
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(ClinicSignup signup, CancellationToken cancellationToken = default) =>
        await _context.ClinicSignups.AddAsync(signup, cancellationToken);

    public Task UpdateAsync(ClinicSignup signup, CancellationToken cancellationToken = default)
    {
        // The guarded form ClinicRepository and PatientRepository use: Version is mapped onto xmin, so a blind
        // Update on a detached instance sends `WHERE xmin = 0`, matches nothing, and 409s with nobody at fault.
        if (_context.Entry(signup).State == EntityState.Detached)
        {
            _context.ClinicSignups.Update(signup);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes spent rows outright, bounded per call, and <b>does not ride the caller's transaction</b>.
    ///
    /// <para>It used to materialise every spent row and stage a <c>RemoveRange</c> on the caller's context. Two
    /// things were wrong with that. A staged DELETE that matches no rows raises <c>DbUpdateConcurrencyException</c>
    /// — which <c>UnitOfWork</c> turns into a <c>ConflictException</c> — so two signups purging the same expired
    /// rows in one tick made the loser's perfectly valid request answer <b>409</b>; and the unbounded load ran on
    /// an anonymous path before any check that could refuse the request. Purging spent rows regardless of whether
    /// the accompanying signup succeeds is harmless, which is what makes the independent delete the right shape.
    /// </para>
    /// </summary>
    public async Task<int> PurgeSpentAsync(
        DateTime nowUtc, TimeSpan consumedRetention, CancellationToken cancellationToken = default)
    {
        var consumedBefore = nowUtc - consumedRetention;

        var ids = await _context.ClinicSignups
            .Where(s => (s.ConsumedAtUtc == null && s.ExpiresAtUtc <= nowUtc)
                        || (s.ConsumedAtUtc != null && s.ConsumedAtUtc <= consumedBefore))
            .OrderBy(s => s.ExpiresAtUtc)
            .Select(s => s.Id)
            .Take(PurgeBatchSize)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return 0;
        }

        return await _context.ClinicSignups
            .Where(s => ids.Contains(s.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
