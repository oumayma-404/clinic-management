using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>« Modifications à vérifier » (AC-5.6). Writes are staged; the caller's unit of work commits.</summary>
public class RelayReviewItemRepository : IRelayReviewItemRepository
{
    private readonly ApplicationDbContext _context;

    public RelayReviewItemRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<RelayReviewItem>> GetForCutAsync(
        Guid relayId, DateTime cutSinceUtc, CancellationToken cancellationToken = default) =>
        await _context.RelayReviewItems
            .Where(i => i.RelayId == relayId && i.CutSinceUtc == cutSinceUtc)
            .OrderBy(i => i.CreatedAtUtc)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

    public Task<PagedResult<RelayReviewItem>> GetPageAsync(
        Guid clinicId, bool includeReviewed, PageRequest? paging, CancellationToken cancellationToken = default) =>
        _context.RelayReviewItems
            .Where(i => i.ClinicId == clinicId && (includeReviewed || i.ReviewedAtUtc == null))
            .OrderByDescending(i => i.CreatedAtUtc)
            .ThenBy(i => i.Id)
            .ToPagedResultAsync(paging, cancellationToken);

    public Task<int> CountPendingAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        _context.RelayReviewItems.CountAsync(i => i.ClinicId == clinicId && i.ReviewedAtUtc == null, cancellationToken);

    public Task<RelayReviewItem?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken cancellationToken = default) =>
        _context.RelayReviewItems.FirstOrDefaultAsync(i => i.ClinicId == clinicId && i.Id == id, cancellationToken);

    public async Task AddRangeAsync(IReadOnlyCollection<RelayReviewItem> items, CancellationToken cancellationToken = default) =>
        await _context.RelayReviewItems.AddRangeAsync(items, cancellationToken);
}
