using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class StockItemRepository : IStockItemRepository
{
    private readonly ApplicationDbContext _context;

    public StockItemRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<StockItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.StockItems
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<StockItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.StockItems
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }


    public async Task<IEnumerable<StockItem>> GetByClinicIdAsync(Guid clinicId, bool lowStockOnly = false, CancellationToken cancellationToken = default)
    {
        var query = _context.StockItems.Where(s => s.ClinicId == clinicId);

        if (lowStockOnly)
        {
            query = query.Where(s => s.CurrentStock <= s.MinimumStockLevel);
        }

        return await query
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<StockItem>> GetLowStockItemsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.StockItems
            .Where(s => s.CurrentStock <= s.MinimumStockLevel)
            .OrderBy(s => s.CurrentStock)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<StockItem>> GetOutOfStockItemsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.StockItems
            .Where(s => s.CurrentStock == 0)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<StockItem> AddAsync(StockItem item, CancellationToken cancellationToken = default)
    {
        await _context.StockItems.AddAsync(item, cancellationToken);
        return item;
    }

    public Task UpdateAsync(StockItem item, CancellationToken cancellationToken = default)
    {
        _context.StockItems.Update(item);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await GetByIdAsync(id, cancellationToken);
        if (item != null)
        {
            _context.StockItems.Remove(item);
        }
    }
}



