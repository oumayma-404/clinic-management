using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class AuditEntryRepository : IAuditEntryRepository
{
    private readonly ApplicationDbContext _context;

    public AuditEntryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AuditEntry>> GetFilteredAsync(
        Guid clinicId,
        string? entityType = null,
        string? entityId = null,
        DateTime? from = null,
        DateTime? to = null,
        AuditAction? action = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AuditEntries
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId);

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            // Exact, case-insensitive: the value comes from `GetRecordedEntityTypesAsync`, so it is a CLR type
            // name and not free text. A LIKE here would make « Invoice » also match « InvoiceLine » if a child
            // ever became auditable.
            var normalized = entityType.Trim();
            query = query.Where(a => EF.Functions.ILike(a.EntityType, normalized));
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            var normalized = entityId.Trim();
            query = query.Where(a => a.EntityId == normalized);
        }

        // Inclusive on both ends, matching every money read in this codebase. The caller derives the bounds from
        // `ClinicClock`, so `to` is the last tick of the clinic's day rather than the next midnight.
        if (from.HasValue)
        {
            query = query.Where(a => a.OccurredAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(a => a.OccurredAt <= to.Value);
        }

        if (action.HasValue)
        {
            query = query.Where(a => a.Action == action.Value);
        }

        // `Id` last, and not decoratively: one save writes several rows with the identical `OccurredAt`, so
        // `OFFSET` over `OccurredAt` alone could show a row on two pages and skip another entirely.
        return await query
            .OrderByDescending(a => a.OccurredAt)
            .ThenBy(a => a.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetRecordedEntityTypesAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AuditEntries
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId)
            .Select(a => a.EntityType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRangeAsync(
        IReadOnlyCollection<AuditEntry> entries,
        CancellationToken cancellationToken = default)
    {
        await _context.AuditEntries.AddRangeAsync(entries, cancellationToken);
    }
}
