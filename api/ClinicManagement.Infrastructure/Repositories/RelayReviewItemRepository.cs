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

    public async Task AddRangeAsync(IReadOnlyCollection<RelayReviewItem> items, CancellationToken cancellationToken = default) =>
        await _context.RelayReviewItems.AddRangeAsync(items, cancellationToken);
}
