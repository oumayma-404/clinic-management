using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class BackupRunRepository : IBackupRunRepository
{
    private readonly ApplicationDbContext _context;

    public BackupRunRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BackupRun?> GetLastSuccessfulAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.BackupRuns
            .Where(b => b.ClinicId == clinicId && b.Outcome == BackupOutcome.Succeeded)
            // Ordered on CompletedAt, not StartedAt: a long dump that began before a later short one and
            // finished after it would otherwise report the wrong « dernière sauvegarde réussie ». On a success
            // CompletedAt is never null, so the fallback only exists to satisfy the type.
            .OrderByDescending(b => b.CompletedAt ?? b.StartedAt)
            .ThenBy(b => b.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<BackupRun?> GetLastRunAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.BackupRuns
            .Where(b => b.ClinicId == clinicId)
            .OrderByDescending(b => b.StartedAt)
            .ThenBy(b => b.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<BackupRun>> GetHistoryAsync(
        Guid clinicId, PageRequest? paging, CancellationToken cancellationToken = default) =>
        await _context.BackupRuns
            .Where(b => b.ClinicId == clinicId)
            .OrderByDescending(b => b.StartedAt)
            // `.ThenBy(Id)` is not decoration: the scheduled job and a manual click can start in the same tick,
            // and OFFSET over a non-unique sort shows one row twice and skips another.
            .ThenBy(b => b.Id)
            .ToPagedResultAsync(paging, cancellationToken);

    public async Task AddAsync(BackupRun run, CancellationToken cancellationToken = default)
    {
        await _context.BackupRuns.AddAsync(run, cancellationToken);
    }

    public Task UpdateAsync(BackupRun run, CancellationToken cancellationToken = default)
    {
        // Attach only a DETACHED instance — the same rule the other repositories document: calling Update() on
        // a tracked entity re-marks every property modified, and on a never-loaded one the xmin token reads as 0,
        // producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        if (_context.Entry(run).State == EntityState.Detached)
        {
            _context.BackupRuns.Update(run);
        }

        return Task.CompletedTask;
    }
}
