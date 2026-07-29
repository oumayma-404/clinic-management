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
            // The lots are part of the aggregate now (AC-P4.1): FEFO consumption and the earliest-relevant
            // expiry both read them, so a load without them would silently consume nothing and report no date.
            .Include(s => s.Batches)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<StockItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.StockItems
            .Include(s => s.Batches)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }


    public async Task<IEnumerable<StockItem>> GetByClinicIdAsync(Guid clinicId, bool lowStockOnly = false, CancellationToken cancellationToken = default)
    {
        var query = _context.StockItems
            .Include(s => s.Batches)
            .Where(s => s.ClinicId == clinicId);

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
            .Include(s => s.Batches)
            .Where(s => s.CurrentStock <= s.MinimumStockLevel)
            .OrderBy(s => s.CurrentStock)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<StockItem>> GetOutOfStockItemsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.StockItems
            .Include(s => s.Batches)
            .Where(s => s.CurrentStock == 0)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountLowStockAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Same predicate as GetByClinicIdAsync's lowStockOnly branch, so the card and the filtered list it links to
        // can never report different numbers. No Include: a count needs no lots.
        return await _context.StockItems
            .Where(s => s.ClinicId == clinicId && s.CurrentStock <= s.MinimumStockLevel)
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountExpiringSoonAsync(
        Guid clinicId, int leadDays, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        // Mirrors StockBatch.IsExpired / IsExpiringSoon: a dated lot with stock left whose expiry falls on or before
        // the lead horizon — an already-expired lot included, since that is the more urgent case of the same alert.
        // RemainingQuantity > 0 is load-bearing: an emptied expired lot has nothing left to waste.
        var horizon = asOfUtc.AddDays(leadDays);

        return await _context.StockItems
            .Where(s => s.ClinicId == clinicId
                        && s.Batches.Any(b => b.RemainingQuantity > 0
                                              && b.ExpiryDate != null
                                              && b.ExpiryDate <= horizon))
            .CountAsync(cancellationToken);
    }

    public async Task<StockItem> AddAsync(StockItem item, CancellationToken cancellationToken = default)
    {
        await _context.StockItems.AddAsync(item, cancellationToken);
        return item;
    }

    public Task UpdateAsync(StockItem item, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(item);
        if (entry.State == EntityState.Detached)
        {
            _context.StockItems.Update(item);
        }
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



