using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IDoctorRepository
{
    Task<Doctor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Doctor>> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<Doctor?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task AddAsync(Doctor entity, CancellationToken cancellationToken = default);
    void Update(Doctor entity);
    void Remove(Doctor entity);
}




