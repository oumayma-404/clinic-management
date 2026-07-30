using ClinicManagement.Domain.Entities;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for the per-clinic medication catalog. Medications + their active ingredients are per-clinic
/// reference data (medications have <c>ClinicId</c> and are clinic-filtered). Mutations only stage changes — the caller
/// commits via <see cref="ClinicManagement.Application.Common.Interfaces.IUnitOfWork"/>.
/// </summary>
public interface IMedicationCatalogRepository
{
    Task<Medication?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// The drug catalog. <paramref name="searchTerm"/> is matched in SQL over brand, form, strength <b>and the
    /// DCI child rows</b> — prescribers search by molecule as often as by brand. <paramref name="paging"/> of
    /// null returns every match, which the ordonnance picker needs.
    /// </summary>
    Task<PagedResult<Medication>> GetAllAsync(
        bool includeInactive = false,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);
    Task<bool> BrandExistsAsync(string brandName, string form, string strength, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<Medication> AddAsync(Medication medication, CancellationToken cancellationToken = default);
    Task UpdateAsync(Medication medication, CancellationToken cancellationToken = default);
}
