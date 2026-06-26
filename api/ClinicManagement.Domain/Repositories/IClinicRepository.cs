using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IClinicRepository
{
    Task<Clinic?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Clinic?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Clinic?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IEnumerable<Clinic>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Clinic> AddAsync(Clinic clinic, CancellationToken cancellationToken = default);
    Task UpdateAsync(Clinic clinic, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);
}




