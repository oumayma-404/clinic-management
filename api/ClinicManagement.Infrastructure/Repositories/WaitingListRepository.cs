using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class WaitingListRepository : IWaitingListRepository
{
    private readonly ApplicationDbContext _context;

    public WaitingListRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<WaitingListEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.WaitingListEntries
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<WaitingListEntry>> GetByClinicIdAsync(Guid clinicId, bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = _context.WaitingListEntries
            .Include(w => w.Patient)
            .Where(w => w.ClinicId == clinicId);

        if (activeOnly)
        {
            query = query.Where(w => w.Status == WaitingListStatus.Waiting);
        }

        return await query
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<WaitingListEntry> AddAsync(WaitingListEntry entry, CancellationToken cancellationToken = default)
    {
        await _context.WaitingListEntries.AddAsync(entry, cancellationToken);
        return entry;
    }

    public Task UpdateAsync(WaitingListEntry entry, CancellationToken cancellationToken = default)
    {
        _context.WaitingListEntries.Update(entry);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await GetByIdAsync(id, cancellationToken);
        if (entry != null)
        {
            _context.WaitingListEntries.Remove(entry);
        }
    }
}
