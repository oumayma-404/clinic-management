using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for the per-clinic dental act catalog (chapitre DCH). Per-clinic reference data (has
/// <c>ClinicId</c>, clinic-filtered). Mutations only stage changes — the caller commits via
/// <see cref="ClinicManagement.Application.Common.Interfaces.IUnitOfWork"/>.
/// </summary>
public interface IDentalActCodeRepository
{
    Task<DentalActCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<DentalActCode>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<bool> CodeActeExistsAsync(string codeActe, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> AnyProvisionalAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<DentalActCode>> GetProvisionalAsync(CancellationToken cancellationToken = default);
    Task<DentalActCode> AddAsync(DentalActCode entry, CancellationToken cancellationToken = default);
    Task UpdateAsync(DentalActCode entry, CancellationToken cancellationToken = default);
}
