using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IProcedureTypeRepository
{
    Task<ProcedureType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProcedureType?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// The catalog list, optionally including deactivated acts, optionally filtered by a free-text term matched
    /// in SQL over name and description, optionally one page. <c>paging: null</c> = every match, which the
    /// appointment form's act picker and the AI dispatcher both need.
    /// <para>Replaced <c>GetAllAsync</c>/<c>GetActiveAsync</c>: they differed by a single <c>Where</c>, and
    /// paging plus a search predicate would have had to be added to both.</para>
    /// </summary>
    Task<PagedResult<ProcedureType>> GetFilteredAsync(
        Guid clinicId,
        bool includeInactive = false,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<ProcedureType> AddAsync(ProcedureType procedureType, CancellationToken cancellationToken = default);
    Task UpdateAsync(ProcedureType procedureType, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}










