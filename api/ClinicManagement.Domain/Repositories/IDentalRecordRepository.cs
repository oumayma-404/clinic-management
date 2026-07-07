using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IDentalRecordRepository
{
    Task<DentalRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<DentalRecord>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<DentalRecord> AddAsync(DentalRecord dentalRecord, CancellationToken cancellationToken = default);
    Task UpdateAsync(DentalRecord dentalRecord, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}









