using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IStockMovementRepository
{
    /// <summary>Newest-first movements for a stock item.</summary>
    Task<IReadOnlyList<StockMovement>> GetByStockItemAsync(Guid stockItemId, CancellationToken cancellationToken = default);

    Task<StockMovement> AddAsync(StockMovement movement, CancellationToken cancellationToken = default);
}
