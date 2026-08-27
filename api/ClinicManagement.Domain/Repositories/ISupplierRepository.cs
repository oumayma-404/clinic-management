using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// What a fournisseur is referenced by, per table.
/// <para>
/// ⚠️ <b>Two counts and not one total</b>, because AC-4's refusal has to name what is actually in the way: « 3
/// articles de stock » sends somebody to the stockroom, « 3 bons de prothèse » to the laboratory screen, and a
/// bare « 3 » sends them looking in the wrong place. <see cref="Total"/> exists for the one caller that only
/// needs to know whether anything at all points here.
/// </para>
/// </summary>
public readonly record struct SupplierUsage(int StockItems, int LabOrders)
{
    public int Total => StockItems + LabOrders;
    public bool IsReferenced => Total > 0;
}

public interface ISupplierRepository
{
    Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The clinic's fournisseurs. <paramref name="searchTerm"/> is matched in SQL over nom, catégorie, téléphone
    /// and adresse; <paramref name="paging"/> of null returns every match (the stock form's picker needs the
    /// whole set, and the CSV export re-sends the screen's query unpaged).
    /// </summary>
    /// <param name="includeInactive">
    /// False — the default the pickers want — hides a deactivated supplier. It never hides one from an article
    /// that already names it: that resolution goes through <see cref="GetByIdsAsync"/>, which ignores the flag
    /// (AC-4, EC-4).
    /// </param>
    Task<PagedResult<Supplier>> GetFilteredAsync(
        Guid clinicId,
        string? searchTerm = null,
        string? category = null,
        bool includeInactive = false,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The suppliers behind a set of ids, clinic-filtered — one read for a whole page of stock articles rather
    /// than one per row, the `list-pagination` companion-read rule. <b>Deactivated suppliers are included</b>:
    /// this resolves a link that already exists, and a row whose supplier vanished from its own display would be
    /// the deactivation erasing history that AC-4 forbids.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Supplier>> GetByIdsAsync(
        Guid clinicId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// The supplier of this clinic whose nom folds onto <paramref name="name"/>, for AC-1's duplicate refusal.
    /// <para>
    /// ⚠️ Matched <b>accent- and case-insensitively</b>, unlike the unique index behind it, which is exact. The
    /// index is the backstop against a race; this is the check that produces a French message naming the record
    /// somebody already created — and « Dentalex » / « dentalex » being two rows is exactly the duplicate a
    /// practice creates by accident.
    /// </para>
    /// </summary>
    Task<Supplier?> FindByNameAsync(
        Guid clinicId, string name, Guid? excludingId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every distinct catégorie in use in this clinic, sorted — the options the pickers union with
    /// <c>SupplierCategories.Canonical</c>. Includes the categories of <b>deactivated</b> suppliers, for the
    /// reason <c>ProcedureTypeRepository.GetCategoriesAsync</c> does: a category the practice files suppliers
    /// under does not stop being one because a dépôt closed.
    /// </summary>
    /// <param name="activeOnly">
    /// Restrict to categories carried by an ACTIVE supplier — what the filter's chips need.
    ///
    /// <para>⚠️ The two consumers ask different questions and one answer served both wrongly. The form's
    /// suggestion list wants every label ever filed (the default, <c>false</c>); the filter's chips want only what
    /// can still narrow the list on screen, whose default read excludes deactivated rows — so retiring the only
    /// laboratory of a kind left a chip whose every click answered « Aucun fournisseur pour ces filtres ».</para>
    /// </param>
    Task<IReadOnlyList<string>> GetCategoriesAsync(
        Guid clinicId, bool activeOnly = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// What each of <paramref name="supplierIds"/> is referenced by — one <c>GROUP BY</c> per table over the
    /// page, never a count per row. A supplier referenced by nothing is absent from the dictionary rather than
    /// present with a zeroed <see cref="SupplierUsage"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, SupplierUsage>> GetUsageAsync(
        Guid clinicId, IReadOnlyCollection<Guid> supplierIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The counts AC-4's delete refusal names. A separate call from <see cref="GetUsageAsync"/> on purpose: the
    /// delete must count against the database at the moment it runs, not against a figure the client read when it
    /// drew the list.
    /// </summary>
    Task<SupplierUsage> GetUsageAsync(Guid supplierId, CancellationToken cancellationToken = default);

    Task<Supplier> AddAsync(Supplier supplier, CancellationToken cancellationToken = default);
    Task UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default);
    Task DeleteAsync(Supplier supplier, CancellationToken cancellationToken = default);
}
