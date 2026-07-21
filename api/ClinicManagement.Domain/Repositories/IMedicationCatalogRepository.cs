using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for the global medication catalog. Medications + their active ingredients are global
/// reference data (no <c>ClinicId</c>, not clinic-filtered). Mutations only stage changes — the caller
/// commits via <see cref="ClinicManagement.Application.Common.Interfaces.IUnitOfWork"/>.
/// </summary>
public interface IMedicationCatalogRepository
{
    Task<Medication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Medication>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<bool> BrandExistsAsync(string brandName, string form, string strength, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<Medication> AddAsync(Medication medication, CancellationToken cancellationToken = default);
    Task UpdateAsync(Medication medication, CancellationToken cancellationToken = default);
}
