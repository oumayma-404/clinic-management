using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
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

    public async Task<PagedResult<WaitingListEntry>> GetByClinicIdAsync(
        Guid clinicId,
        bool activeOnly = true,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.WaitingListEntries
            .Include(w => w.Patient)
            .Where(w => w.ClinicId == clinicId);

        if (activeOnly)
        {
            query = query.Where(w => w.Status == WaitingListStatus.Waiting);
        }

        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(w =>
                EF.Functions.ILike(SqlSearch.Unaccent(w.Patient!.FirstName + " " + w.Patient.LastName)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(w.Note)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(w.DesiredTimeframe)!, pattern, SqlSearch.EscapeString));
        }

        return await query
            .OrderByDescending(w => w.Priority)
            .ThenBy(w => w.CreatedAt)
            .ThenBy(w => w.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<int> CountWaitingAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Same predicate as GetByClinicIdAsync's activeOnly branch, so the dashboard card and the salle d'attente
        // it links to agree. No Include: a count needs no patient names.
        return await _context.WaitingListEntries
            .Where(w => w.ClinicId == clinicId && w.Status == WaitingListStatus.Waiting)
            .CountAsync(cancellationToken);
    }

    public async Task<WaitingListEntry> AddAsync(WaitingListEntry entry, CancellationToken cancellationToken = default)
    {
        await _context.WaitingListEntries.AddAsync(entry, cancellationToken);
        return entry;
    }

    public Task UpdateAsync(WaitingListEntry entry, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var trackingEntry = _context.Entry(entry);
        if (trackingEntry.State == EntityState.Detached)
        {
            _context.WaitingListEntries.Update(entry);
        }
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
