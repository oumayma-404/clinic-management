using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IPatientRepository
{
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Patient?> GetByIdWithAppointmentsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Patient>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Patient>> GetFlaggedPatientsAsync(CancellationToken cancellationToken = default);
    Task<Patient> AddAsync(Patient patient, CancellationToken cancellationToken = default);
    Task UpdateAsync(Patient patient, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}



