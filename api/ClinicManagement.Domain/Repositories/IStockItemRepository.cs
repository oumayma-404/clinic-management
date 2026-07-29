using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IStockItemRepository
{
    Task<StockItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<StockItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<StockItem>> GetByClinicIdAsync(Guid clinicId, bool lowStockOnly = false, CancellationToken cancellationToken = default);
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
    Task<StockItem> AddAsync(StockItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(StockItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}



