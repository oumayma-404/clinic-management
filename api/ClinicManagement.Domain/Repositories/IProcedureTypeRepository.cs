using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IProcedureTypeRepository
{
    Task<ProcedureType?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProcedureType?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// The catalog list, optionally including deactivated acts, optionally narrowed to one
    /// <paramref name="category"/>, optionally filtered by a free-text term matched in SQL over name, category and
    /// description, optionally one page. <c>paging: null</c> = every match, which the appointment form's act
    /// picker and the AI dispatcher both need.
    /// <para>Replaced <c>GetAllAsync</c>/<c>GetActiveAsync</c>: they differed by a single <c>Where</c>, and
    /// paging plus a search predicate would have had to be added to both.</para>
    /// <para>
    /// ⚠️ <paramref name="category"/> is a repository argument for the reason <paramref name="searchTerm"/> is:
    /// narrowing an already-cut page in memory shrinks pages unpredictably, so « Endodontie » on page 1 of a
    /// paged catalogue would show three rows out of twenty-five.
    /// </para>
    /// <para>
    /// Ordered by category (unfiled last), then name. That is <b>alphabetical</b> by category, not the clinical
    /// session order the act pickers browse in — this read is paged, and a 12-branch CASE in SQL to reproduce a
    /// display order would put clinical knowledge in the persistence layer. The pickers hold the whole catalogue
    /// and sort it themselves; see <c>web/components/procedure-categories.ts</c>.
    /// </para>
    /// </summary>
    Task<PagedResult<ProcedureType>> GetFilteredAsync(
        Guid clinicId,
        bool includeInactive = false,
        string? searchTerm = null,
        string? category = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every distinct category this clinic has actually filed an act under, alphabetically.
    /// <para>
    /// Feeds the catalogue's category filter and the act form's suggestion list. Deliberately includes the
    /// categories of <b>deactivated</b> acts: a discipline the practice files work under does not stop being one
    /// because one act in it was archived, and a suggestion list missing it is what makes an admin retype — the
    /// exact drift <see cref="Services.ProcedureTypeCategories.Normalize"/> exists to prevent.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<string>> GetCategoriesAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task<ProcedureType> AddAsync(ProcedureType procedureType, CancellationToken cancellationToken = default);
    Task UpdateAsync(ProcedureType procedureType, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}










