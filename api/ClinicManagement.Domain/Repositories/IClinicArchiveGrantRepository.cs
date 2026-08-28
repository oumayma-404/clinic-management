using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Reads and writes the archive device grants (<c>clinic-archive-auto-copy</c>), on
/// <see cref="IClinicRecoveryPointRepository"/>'s shape.
/// </summary>
public interface IClinicArchiveGrantRepository
{
    /// <summary>Every grant of a cabinet, revoked ones included — the list is how an owner audits what may pull.</summary>
    Task<IReadOnlyList<ClinicArchiveGrant>> ListAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task<ClinicArchiveGrant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The grant whose secret hashes to <paramref name="secretHash"/>, or null.
    ///
    /// <para>⚠️ <b>Matched on the hash and never on the id</b>, so a caller presenting a grant proves possession of
    /// the secret rather than knowledge of a guid. It reads <b>system-wide</b> because the request carrying it has
    /// no session and therefore no tenant scope yet — the clinic comes back <i>from</i> the row, and the caller
    /// then compares it to the cabinet being served (AC-4).</para>
    /// </summary>
    Task<ClinicArchiveGrant?> FindBySecretHashAsync(string secretHash, CancellationToken cancellationToken = default);

    Task AddAsync(ClinicArchiveGrant grant, CancellationToken cancellationToken = default);

    Task UpdateAsync(ClinicArchiveGrant grant, CancellationToken cancellationToken = default);
}
