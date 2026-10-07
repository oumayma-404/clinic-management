using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IClinicRelayRepository
{
    /// <summary>The clinic's one non-retired PC de secours row, if any (filtered).</summary>
    Task<ClinicRelay?> GetCurrentForClinicAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>The clinic's newest row, retired included — what « Paramètres » shows after a retirement.</summary>
    Task<ClinicRelay?> GetLatestForClinicAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>By id, ignoring the clinic filter: the relay's own token carries no session clinic.</summary>
    Task<ClinicRelay?> GetByIdAcrossClinicsAsync(Guid relayId, CancellationToken cancellationToken = default);

    /// <summary>By pairing-code hash, ignoring the clinic filter: the installer has no session at all.</summary>
    Task<ClinicRelay?> FindByPairingCodeAcrossClinicsAsync(string codeHash, CancellationToken cancellationToken = default);

    /// <summary>Every non-retired row of every clinic — the watcher's read (caller declares system-wide).</summary>
    Task<IReadOnlyList<ClinicRelay>> GetLiveAsync(CancellationToken cancellationToken = default);

    Task AddAsync(ClinicRelay relay, CancellationToken cancellationToken = default);
}
