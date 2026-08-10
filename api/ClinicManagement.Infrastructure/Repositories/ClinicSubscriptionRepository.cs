using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// The entitlement and its ledger. No <c>IgnoreQueryFilters()</c> anywhere: both tables carry a non-nullable
/// <c>ClinicId</c> and are filtered, so a caller with no clinic in scope must declare <c>UseSystemWide</c> rather
/// than have this class quietly read across cabinets.
/// </summary>
public class ClinicSubscriptionRepository : IClinicSubscriptionRepository
{
    private readonly ApplicationDbContext _context;

    public ClinicSubscriptionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ClinicSubscription?> GetByClinicAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.ClinicSubscriptions
            .FirstOrDefaultAsync(s => s.ClinicId == clinicId, cancellationToken);

    /// <summary>
    /// The whole ledger, oldest first with <c>Id</c> as the tie-break. The ordering is not cosmetic: two grants
    /// recorded in the same tick must fold in a stable order, or <c>EndsOn</c> would depend on which row
    /// PostgreSQL happened to return first. <c>SubscriptionLedger</c> re-applies the same order, so neither side
    /// silently depends on the other.
    /// </summary>
    public async Task<IReadOnlyList<SubscriptionPeriod>> GetEntriesAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.SubscriptionPeriods
            .Where(p => p.ClinicId == clinicId)
            .OrderBy(p => p.RecordedAtUtc)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ClinicSubscription subscription, CancellationToken cancellationToken = default) =>
        await _context.ClinicSubscriptions.AddAsync(subscription, cancellationToken);

    public async Task AddEntryAsync(SubscriptionPeriod entry, CancellationToken cancellationToken = default) =>
        await _context.SubscriptionPeriods.AddAsync(entry, cancellationToken);

    public Task UpdateAsync(ClinicSubscription subscription, CancellationToken cancellationToken = default)
    {
        // The guarded form ClinicRepository, PatientRepository and ClinicSignupRepository all use: Version is
        // mapped onto xmin, so a blind Update on a detached instance sends `WHERE xmin = 0`, matches nothing, and
        // 409s with nobody at fault.
        if (_context.Entry(subscription).State == EntityState.Detached)
        {
            _context.ClinicSubscriptions.Update(subscription);
        }

        return Task.CompletedTask;
    }
}
