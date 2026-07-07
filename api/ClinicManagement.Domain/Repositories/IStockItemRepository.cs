using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IStockItemRepository
{
    Task<StockItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<StockItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<StockItem>> GetByClinicIdAsync(Guid clinicId, bool lowStockOnly = false, CancellationToken cancellationToken = default);
    Task<IEnumerable<StockItem>> GetLowStockItemsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<StockItem>> GetOutOfStockItemsAsync(CancellationToken cancellationToken = default);
    Task<StockItem> AddAsync(StockItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(StockItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}



