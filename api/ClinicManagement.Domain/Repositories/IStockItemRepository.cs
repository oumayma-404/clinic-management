using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IStockItemRepository
{
    Task<StockItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<StockItem>> GetAllAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// The stockroom list. <paramref name="searchTerm"/> is matched in SQL over name, category and supplier;
    /// <paramref name="paging"/> of null returns every match (the act-materials picker needs the full set).
    /// </summary>
    /// <param name="category">Exact category match, or null for every category.</param>
    /// <param name="expiringHorizonUtc">
    /// When supplied, only items with a dated lot still holding stock whose expiry falls on or before this instant.
    /// The caller passes the horizon rather than a lead-day count so this predicate stays identical to
    /// <see cref="CountExpiringSoonAsync"/>'s — the chip and the list it filters to must agree.
    /// </param>
    Task<PagedResult<StockItem>> GetByClinicIdAsync(
        Guid clinicId,
        bool lowStockOnly = false,
        string? searchTerm = null,
        string? category = null,
        DateTime? expiringHorizonUtc = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every distinct category in the clinic, sorted — the options for the stockroom's category filter. Clinic-wide
    /// on purpose: derived from a page, the dropdown would only offer the categories that page happened to contain.
    /// </summary>
    Task<IReadOnlyList<string>> GetDistinctCategoriesAsync(
        Guid clinicId, CancellationToken cancellationToken = default);
    Task<IEnumerable<StockItem>> GetLowStockItemsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<StockItem>> GetOutOfStockItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of the clinic's items are at or below their reorder threshold. A <c>COUNT</c>, not
    /// <see cref="GetLowStockItemsAsync"/>'s entity list — the dashboard needs the number, and that method is also
    /// cross-clinic.
    /// </summary>
    Task<int> CountLowStockAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of the clinic's items hold a lot expiring within <paramref name="leadDays"/> of
    /// <paramref name="asOfUtc"/>, counting an already-expired lot too. Only lots with stock remaining count — an
    /// emptied expired lot is not a warning, the same reading <c>StockBatch.IsExpiringSoon</c> applies.
    /// <para>
    /// The caller is responsible for not calling this when the clinic has the alert switched off
    /// (<c>Clinic.StockExpiryLeadDays &lt;= 0</c>), exactly as <c>StockExpiryJob</c> does.
    /// </para>
    /// </summary>
    Task<int> CountExpiringSoonAsync(
        Guid clinicId, int leadDays, DateTime asOfUtc, CancellationToken cancellationToken = default);
    /// <summary>
    /// Which of <paramref name="itemIds"/> currently name a fournisseur, and which one — a light projection with
    /// no <c>Include</c>, because its caller (the bell feed) needs the link and not the aggregate.
    /// <para>
    /// ⚠️ <b>An article with no supplier is absent from the dictionary</b> rather than present with an empty
    /// GUID: a sentinel there would be a supplier id that resolves to nothing, which is the failure mode the four
    /// retired contact sentinels are the precedent for.
    /// </para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetSupplierLinksAsync(
        Guid clinicId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    Task<StockItem> AddAsync(StockItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(StockItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}



