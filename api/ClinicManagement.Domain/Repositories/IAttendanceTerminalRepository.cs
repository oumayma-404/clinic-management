using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IAttendanceTerminalRepository
{
    Task<AttendanceTerminal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The active borne holding this secret, in ANY cabinet. A borne request has no user and so no clinic yet:
    /// this is the read that tells the caller which clinic to scope to.
    /// </summary>
    Task<AttendanceTerminal?> FindActiveBySecretHashAcrossClinicsAsync(
        string secretHash, CancellationToken cancellationToken = default);

    /// <summary>The cabinet's bornes, active first, newest first.</summary>
    Task<IReadOnlyList<AttendanceTerminal>> ListAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task AddAsync(AttendanceTerminal terminal, CancellationToken cancellationToken = default);
}
