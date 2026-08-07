using ClinicManagement.Domain.Entities;

using ClinicManagement.Domain.Common;
namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for the per-clinic dental act catalog (chapitre DCH). Per-clinic reference data (has
/// <c>ClinicId</c>, clinic-filtered). Mutations only stage changes — the caller commits via
/// <see cref="ClinicManagement.Application.Common.Interfaces.IUnitOfWork"/>.
/// </summary>
public interface IDentalActCodeRepository
{
    Task<DentalActCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// The DCH catalog. Category and the free-text term (code, désignation) are matched in SQL; they used to be
    /// applied in the handler after the whole catalog was read. <paramref name="paging"/> of null returns every
    /// match, which the devis and invoice act pickers need.
    /// </summary>
    Task<PagedResult<DentalActCode>> GetAllAsync(
        bool includeInactive = false,
        string? category = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);
    Task<bool> CodeActeExistsAsync(string codeActe, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> AnyProvisionalAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<DentalActCode>> GetProvisionalAsync(CancellationToken cancellationToken = default);
    Task<DentalActCode> AddAsync(DentalActCode entry, CancellationToken cancellationToken = default);
    Task UpdateAsync(DentalActCode entry, CancellationToken cancellationToken = default);
}
