using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class PlatformNewsRepository : IPlatformNewsRepository
{
    private readonly ApplicationDbContext _context;

    public PlatformNewsRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(PlatformNewsBroadcast broadcast, CancellationToken cancellationToken = default) =>
        await _context.PlatformNewsBroadcasts.AddAsync(broadcast, cancellationToken);

    public async Task<PlatformNewsBroadcast?> GetByRequestIdAsync(
        Guid requestId, CancellationToken cancellationToken = default) =>
        await _context.PlatformNewsBroadcasts
            .Include(b => b.Recipients)
            .FirstOrDefaultAsync(b => b.RequestId == requestId, cancellationToken);

    public async Task<PlatformNewsBroadcast?> GetWithRecipientsAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        await _context.PlatformNewsBroadcasts
            .Include(b => b.Recipients)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListUnfinishedIdsAsync(
        DateTime createdBefore, CancellationToken cancellationToken = default) =>
        await _context.PlatformNewsBroadcasts
            .AsNoTracking()
            .Where(b => b.CompletedAtUtc == null && b.CreatedAtUtc < createdBefore)
            .OrderBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.Id)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlatformNewsSummaryRow>> ListRecentAsync(
        int take, CancellationToken cancellationToken = default)
    {
        var broadcasts = await _context.PlatformNewsBroadcasts
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .ThenBy(b => b.Id)
            .Take(take)
            .Select(b => new
            {
                b.Id,
                b.Subject,
                b.CreatedAtUtc,
                b.CompletedAtUtc,
                b.SentByName,
                Clinics = b.Recipients.Select(r => r.ClinicId).Distinct().Count(),
                Recipients = b.Recipients.Count,
                Sent = b.Recipients.Count(r => r.Status == PlatformNewsDeliveryStatus.Sent),
                Failed = b.Recipients.Count(r => r.Status == PlatformNewsDeliveryStatus.Failed),
            })
            .ToListAsync(cancellationToken);

        var ids = broadcasts.Select(b => b.Id).ToList();

        var failedClinics = await _context.PlatformNewsRecipients
            .AsNoTracking()
            .Where(r => ids.Contains(r.BroadcastId) && r.Status == PlatformNewsDeliveryStatus.Failed)
            .Select(r => new { r.BroadcastId, r.ClinicName })
            .Distinct()
            .ToListAsync(cancellationToken);

        var failedByBroadcast = failedClinics
            .GroupBy(f => f.BroadcastId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(f => f.ClinicName).OrderBy(n => n).ToList());

        return broadcasts
            .Select(b => new PlatformNewsSummaryRow(
                b.Id,
                b.Subject,
                b.CreatedAtUtc,
                b.CompletedAtUtc,
                b.SentByName,
                b.Clinics,
                b.Recipients,
                b.Sent,
                b.Failed,
                failedByBroadcast.TryGetValue(b.Id, out var names) ? names : []))
            .ToList();
    }
}
