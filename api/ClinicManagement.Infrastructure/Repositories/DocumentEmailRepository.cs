using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class DocumentEmailRepository : IDocumentEmailRepository
{
    private readonly ApplicationDbContext _context;

    public DocumentEmailRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DocumentEmail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.DocumentEmails.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DocumentEmail>> GetForDocumentAsync(
        Guid clinicId,
        string documentKind,
        Guid documentId,
        CancellationToken cancellationToken = default) =>
        await _context.DocumentEmails
            .Where(e => e.ClinicId == clinicId && e.DocumentKind == documentKind && e.DocumentId == documentId)
            .OrderByDescending(e => e.QueuedAt)
            .ThenByDescending(e => e.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DocumentEmail>> GetQueuedAsync(
        int batchSize, int perClinicBound, CancellationToken cancellationToken = default)
    {
        var queued = _context.DocumentEmails.Where(e => e.Status == DocumentEmailStatus.Queued);

        // Which clinics have work waiting, and how old their oldest waiting row is. Predicate for predicate
        // NotificationRepository.GetDueForDispatchAsync's, because the same starvation was possible here: this scan
        // had no clinic dimension at all, so one practice's backlog consumed every minutely tick (review finding 5).
        var backlog = await queued
            .GroupBy(e => e.ClinicId)
            .Select(g => new { ClinicId = g.Key, Oldest = g.Min(e => e.QueuedAt) })
            .ToListAsync(cancellationToken);

        // A single-clinic install keeps the flat query it had: a fair share between one participant is the whole
        // batch, and the loop below would only add a round trip to prove it.
        if (backlog.Count <= 1)
        {
            return await queued
                .OrderBy(e => e.QueuedAt)
                .ThenBy(e => e.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        var perClinic = Math.Max(1, perClinicBound);
        var collected = new List<DocumentEmail>(batchSize);

        // Oldest-waiting-first between clinics, so a clinic can neither buy priority by queueing more nor lose it
        // by queueing fewer.
        foreach (var clinic in backlog.OrderBy(b => b.Oldest))
        {
            var remaining = batchSize - collected.Count;
            if (remaining <= 0)
            {
                break;
            }

            collected.AddRange(await queued
                .Where(e => e.ClinicId == clinic.ClinicId)
                .OrderBy(e => e.QueuedAt)
                .ThenBy(e => e.Id)
                .Take(Math.Min(perClinic, remaining))
                .ToListAsync(cancellationToken));
        }

        return collected.OrderBy(e => e.QueuedAt).ThenBy(e => e.Id).ToList();
    }

    public async Task<IReadOnlyList<DocumentEmail>> GetBlockedForReviewAsync(
        int take, CancellationToken cancellationToken = default) =>
        await _context.DocumentEmails
            .Where(e => e.Status == DocumentEmailStatus.Blocked)
            .OrderBy(e => e.QueuedAt)
            .ThenBy(e => e.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<DocumentEmailOutboxDepth> GetOutboxDepthAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        var scoped = _context.DocumentEmails.Where(e => e.ClinicId == clinicId);

        var queued = scoped.Where(e => e.Status == DocumentEmailStatus.Queued);

        var queuedCount = await queued.CountAsync(cancellationToken);

        // Min over a nullable projection rather than MinAsync over the value: an empty queue must give null, not
        // an exception.
        var oldestQueuedAt = await queued
            .Select(e => (DateTime?)e.QueuedAt)
            .MinAsync(cancellationToken);

        // Without this figure a growing Queued with an ancient oldest row reads identically to « the dispatcher is
        // not running » — R-1's failure mode, on the endpoint built to tell the two apart.
        var blocked = await scoped.CountAsync(e => e.Status == DocumentEmailStatus.Blocked, cancellationToken);

        var failed = await scoped.CountAsync(e => e.Status == DocumentEmailStatus.Failed, cancellationToken);

        return new DocumentEmailOutboxDepth(queuedCount, blocked, failed, oldestQueuedAt);
    }

    public async Task AddAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default) =>
        await _context.DocumentEmails.AddAsync(documentEmail, cancellationToken);

    public Task UpdateAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default)
    {
        _context.DocumentEmails.Update(documentEmail);
        return Task.CompletedTask;
    }
}
