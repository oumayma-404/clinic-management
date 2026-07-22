using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for the persistent odontogram (<see cref="ToothState"/> treatment entries, many-per-tooth).
/// Child-of-patient; tenant isolation is enforced at the handler by loading the owning patient. Entries are
/// written/removed through the dental-record flow. Mutations only stage changes.
/// </summary>
public interface IToothStateRepository
{
    Task<IEnumerable<ToothState>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ToothState>> GetByDentalRecordIdAsync(Guid dentalRecordId, CancellationToken cancellationToken = default);
    Task<ToothState?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ToothState> AddAsync(ToothState toothState, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
