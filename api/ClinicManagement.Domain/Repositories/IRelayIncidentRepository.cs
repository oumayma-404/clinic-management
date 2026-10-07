using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>The vendor's PC de secours episodes (<c>clinic-pc-copy</c> AC-9.2). Writes are staged; the caller commits.</summary>
public interface IRelayIncidentRepository
{
    /// <summary>Every open episode of every cabinet — the watcher's read (caller declares system-wide).</summary>
    Task<IReadOnlyList<RelayIncident>> GetOpenAsync(CancellationToken cancellationToken = default);

    Task AddAsync(RelayIncident incident, CancellationToken cancellationToken = default);
}
