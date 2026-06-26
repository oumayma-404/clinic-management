using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IProcedureTypeRepository
{
    Task<ProcedureType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProcedureType?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProcedureType>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<ProcedureType>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<ProcedureType> AddAsync(ProcedureType procedureType, CancellationToken cancellationToken = default);
    Task UpdateAsync(ProcedureType procedureType, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}










