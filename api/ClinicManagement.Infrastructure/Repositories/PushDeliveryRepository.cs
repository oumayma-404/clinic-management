using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class PushDeliveryRepository : IPushDeliveryRepository
{
    private readonly ApplicationDbContext _context;

    public PushDeliveryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddRangeAsync(
        IEnumerable<PushDelivery> deliveries, CancellationToken cancellationToken = default) =>
        await _context.PushDeliveries.AddRangeAsync(deliveries, cancellationToken);

    /// <summary>
    /// The per-clinic-bounded due scan. Predicate-for-predicate the reminder outbox's
    /// <c>GetDueForDispatchAsync</c>, deliberately: the two queues starve in the same way, so a fairness fix in
    /// one should be recognisable in the other rather than reinvented.
    /// </summary>
    public async Task<IReadOnlyList<PushDelivery>> GetDueForDispatchAsync(
        int batchSize, int perClinicBound, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        // Served by IX_PushDeliveries_Status_SendNotBefore — predicate and ORDER BY off one index.
        // Blocked rows are absent by construction: that is what the status is for (AC-50).
        var due = _context.PushDeliveries
            .Where(p => p.Status == PushDeliveryStatus.Pending && p.SendNotBefore <= nowUtc);

        var backlog = await due
            .GroupBy(p => p.ClinicId)
            .Select(g => new { ClinicId = g.Key, Oldest = g.Min(p => p.SendNotBefore) })
            .ToListAsync(cancellationToken);

        // A single clinic keeps the flat query: a fair share between one participant is the whole batch, and the
        // loop below would only add round trips to prove it.
        if (backlog.Count <= 1)
        {
            return await due
                .OrderBy(p => p.SendNotBefore)
                .ThenBy(p => p.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        var perClinic = Math.Max(1, perClinicBound);
        var collected = new List<PushDelivery>(batchSize);

        // Oldest-due-first between clinics, so a clinic can neither buy priority by queueing more nor lose it by
        // queueing less.
        foreach (var clinic in backlog.OrderBy(b => b.Oldest))
        {
            var remaining = batchSize - collected.Count;
            if (remaining <= 0)
            {
                break;
            }

            var slice = await due
                .Where(p => p.ClinicId == clinic.ClinicId)
                .OrderBy(p => p.SendNotBefore)
                .ThenBy(p => p.Id)
                .Take(Math.Min(perClinic, remaining))
                .ToListAsync(cancellationToken);

            collected.AddRange(slice);
        }

        return collected.OrderBy(p => p.SendNotBefore).ThenBy(p => p.Id).ToList();
    }

    public async Task<IReadOnlyList<PushDelivery>> GetBlockedForReviewAsync(
        int batchSize, CancellationToken cancellationToken = default) =>
        await _context.PushDeliveries
            .Where(p => p.Status == PushDeliveryStatus.Blocked)
            .OrderBy(p => p.SendNotBefore)
            .ThenBy(p => p.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public Task UpdateAsync(PushDelivery delivery, CancellationToken cancellationToken = default)
    {
        _context.PushDeliveries.Update(delivery);
        return Task.CompletedTask;
    }

    public async Task<int> PurgeTerminalOlderThanAsync(
        DateTime olderThanUtc, CancellationToken cancellationToken = default) =>
        await _context.PushDeliveries
            .Where(p => (p.Status == PushDeliveryStatus.Sent || p.Status == PushDeliveryStatus.Failed)
                        && p.CreatedAt < olderThanUtc)
            .ExecuteDeleteAsync(cancellationToken);
}
