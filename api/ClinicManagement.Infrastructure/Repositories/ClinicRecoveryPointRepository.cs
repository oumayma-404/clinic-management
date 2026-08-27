using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// The recovery-point ledger over EF Core (<c>clinic-recovery-points</c>).
///
/// <para>⚠️ <b>No <c>IgnoreQueryFilters()</c> anywhere, and none is needed.</b> <see cref="ClinicRecoveryPoint"/>
/// carries a non-nullable <c>ClinicId</c> and is filtered, so a caller with no clinic in scope has to declare
/// <c>UseSystemWide</c> — which the daily pass does — rather than have this class quietly read across cabinets.
/// Every method takes the clinic as a parameter regardless: that remains the authoritative check, as everywhere in
/// this layer.</para>
///
/// <para>⚠️ Every ordered read ends on <c>Id</c>. A cabinet's nightly pass can write two rows in the same tick (a
/// failure and its retry), so ordering on <c>StartedAt</c> alone leaves their relative order to whatever PostgreSQL
/// returns — and the list would reshuffle between renders.</para>
/// </summary>
public class ClinicRecoveryPointRepository : IClinicRecoveryPointRepository
{
    private readonly ApplicationDbContext _context;

    public ClinicRecoveryPointRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ClinicRecoveryPoint?> GetLatestAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.ClinicRecoveryPoints
            .Where(p => p.ClinicId == clinicId)
            .OrderByDescending(p => p.StartedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ClinicRecoveryPoint?> GetLastSuccessfulAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.ClinicRecoveryPoints
            .Where(p => p.ClinicId == clinicId && p.Outcome == BackupOutcome.Succeeded)
            .OrderByDescending(p => p.StartedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ClinicRecoveryPoint?> GetByIdAsync(
        Guid clinicId, Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ClinicRecoveryPoints
            .FirstOrDefaultAsync(p => p.Id == id && p.ClinicId == clinicId, cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicRecoveryPoint>> ListAsync(
        Guid clinicId, int limit, CancellationToken cancellationToken = default)
    {
        return await _context.ClinicRecoveryPoints
            .Where(p => p.ClinicId == clinicId)
            .OrderByDescending(p => p.StartedAt)
            .ThenByDescending(p => p.Id)
            .Take(limit < 1 ? 1 : limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicRecoveryPoint>> GetPrunableAsync(
        Guid clinicId, int keepCount, CancellationToken cancellationToken = default)
    {
        // ⚠️ Succeeded only, on both halves of the question: a failed row must neither consume the retention budget
        // (a bad week would silently prune away every good point) nor be pruned by it (that would erase the record of
        // the failures themselves — the rows that distinguish « personne n'en a » from « il essaie et il échoue »).
        return await _context.ClinicRecoveryPoints
            .Where(p => p.ClinicId == clinicId && p.Outcome == BackupOutcome.Succeeded)
            .OrderByDescending(p => p.StartedAt)
            .ThenByDescending(p => p.Id)
            .Skip(keepCount < 1 ? 1 : keepCount)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ClinicRecoveryPoint point, CancellationToken cancellationToken = default)
    {
        await _context.ClinicRecoveryPoints.AddAsync(point, cancellationToken);
    }

    public Task UpdateAsync(ClinicRecoveryPoint point, CancellationToken cancellationToken = default)
    {
        // Guarded like ClinicSignupRepository's: an Update() on an already-tracked entity is a no-op that costs
        // nothing, while calling it on a detached one whose xmin is 0 would stage a concurrency token of zero and
        // fail the save against every real row.
        if (_context.Entry(point).State == EntityState.Detached)
        {
            _context.ClinicRecoveryPoints.Update(point);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(ClinicRecoveryPoint point, CancellationToken cancellationToken = default)
    {
        _context.ClinicRecoveryPoints.Remove(point);
        return Task.CompletedTask;
    }
}
