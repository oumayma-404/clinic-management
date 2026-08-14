using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The recovery-point ledger (<c>clinic-recovery-points</c>). Read by « Sauvegarde » to list what a cabinet can
/// restore from, by the restore command to resolve one, and by the daily pass to decide whether today's is due and
/// which old ones to prune.
/// </summary>
public interface IClinicRecoveryPointRepository
{
    /// <summary>
    /// The clinic's most recent point of <b>any</b> outcome — what « il essaie et il échoue » is read from, and what
    /// the due-check consults so a crashed <c>Running</c> row is not joined by a second attempt within the quiet
    /// window.
    /// </summary>
    Task<ClinicRecoveryPoint?> GetLatestAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clinic's most recent <b>successful</b> point, or null if it has never had one. Success and not
    /// « most recent attempt », for <c>IBackupRunRepository.GetLastSuccessfulAsync</c>'s reason: a night of failures
    /// does not move the last-good moment.
    /// </summary>
    Task<ClinicRecoveryPoint?> GetLastSuccessfulAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One point by id, scoped to the caller's clinic — the restore's lookup.
    ///
    /// <para>The clinic is a <b>parameter</b> rather than left to the global query filter: this resolves a storage
    /// key that is about to be read and applied to a cabinet's records, so the tenant check is stated at the read
    /// that matters rather than inherited from a backstop.</para>
    /// </summary>
    Task<ClinicRecoveryPoint?> GetByIdAsync(
        Guid clinicId, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clinic's points, newest first, capped at <paramref name="limit"/>.
    ///
    /// <para>⚠️ <b>Capped rather than paged</b>, and that is a decision rather than a shortcut: retention keeps
    /// <see cref="ClinicRecoveryPoint.RetentionCount"/> succeeded points, so the list is bounded by construction and
    /// its only unbounded dimension is a run of failures — which is exactly what somebody opening this list needs to
    /// see all of. A pager over a list that is normally seven rows long would be furniture.</para>
    /// </summary>
    Task<IReadOnlyList<ClinicRecoveryPoint>> ListAsync(
        Guid clinicId, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clinic's <b>succeeded</b> points beyond <paramref name="keepCount"/>, oldest first — what retention
    /// deletes.
    ///
    /// <para>⚠️ <b>Succeeded only</b>, so a failed row is never counted toward the budget nor pruned by it. Both
    /// halves matter: counting failures would let a bad week silently prune away every good point, and pruning them
    /// would erase the record of the failures themselves. They age out with the clinic, not with this.</para>
    /// </summary>
    Task<IReadOnlyList<ClinicRecoveryPoint>> GetPrunableAsync(
        Guid clinicId, int keepCount, CancellationToken cancellationToken = default);

    Task AddAsync(ClinicRecoveryPoint point, CancellationToken cancellationToken = default);
    Task UpdateAsync(ClinicRecoveryPoint point, CancellationToken cancellationToken = default);
    Task RemoveAsync(ClinicRecoveryPoint point, CancellationToken cancellationToken = default);
}
