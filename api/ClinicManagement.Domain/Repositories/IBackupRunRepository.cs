using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The backup ledger (L4d). Read by « Paramètres » for the headline « Dernière sauvegarde réussie », by
/// <c>GET /api/backup/history</c>, and by the daily job's staleness check.
/// </summary>
public interface IBackupRunRepository
{
    /// <summary>
    /// The clinic's most recent <b>successful</b> run, or null if it has never had one.
    ///
    /// <para>Success and not "most recent attempt", because that is the question: a night of failures does not
    /// move the last-good moment, and the staleness alert must fire on the good one or a clinic failing every
    /// night for a week reads as freshly backed up.</para>
    /// </summary>
    Task<BackupRun?> GetLastSuccessfulAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>The most recent run of any outcome — what « il essaie et il échoue » is read from.</summary>
    Task<BackupRun?> GetLastRunAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the clinic's runs, newest first. Always paged, like the audit ledger: the table grows with
    /// every night the practice operates and there is no caller for all of it.
    /// </summary>
    Task<PagedResult<BackupRun>> GetHistoryAsync(
        Guid clinicId, PageRequest? paging, CancellationToken cancellationToken = default);

    Task AddAsync(BackupRun run, CancellationToken cancellationToken = default);
    Task UpdateAsync(BackupRun run, CancellationToken cancellationToken = default);
}
