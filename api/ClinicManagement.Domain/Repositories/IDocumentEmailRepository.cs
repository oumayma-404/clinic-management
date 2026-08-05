using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// How deep the document-email outbox is (multi-tenant-cloud US-6, <c>GET /api/outbox</c>).
///
/// <para>⚠️ <b>No « due » figure, unlike its two sibling outboxes, and that is not an omission.</b> A
/// <c>DocumentEmail</c> carries no scheduled instant — <c>GetQueuedAsync</c> takes every queued row oldest-first —
/// so every queued row is due by definition, and <see cref="OldestQueuedAt"/> alone answers « is the job
/// draining? ». Inventing a due count equal to the queued count would be a field that looks like a comparison and
/// is not one.</para>
/// </summary>
public record DocumentEmailOutboxDepth(int Queued, int Failed, DateTime? OldestQueuedAt);

/// <summary>
/// The document-email outbox. Staged like every other repository — the caller commits through
/// <c>IUnitOfWork</c>.
/// </summary>
public interface IDocumentEmailRepository
{
    Task<DocumentEmail?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The send history of one document, newest first — what the UI shows under « Envois par email ».
    /// Scoped to the clinic so a crafted document id cannot read another cabinet's sends.
    /// </summary>
    Task<IReadOnlyList<DocumentEmail>> GetForDocumentAsync(
        Guid clinicId,
        string documentKind,
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queued rows due for a dispatch attempt, oldest first and <b>bounded</b> — the reminder and e-invoice
    /// outboxes are both batch-capped for the same reason: one tick must not try to send an unbounded backlog.
    /// Crosses clinics deliberately (the job runs with no clinic in scope, so the global filter is inactive).
    /// </summary>
    Task<IReadOnlyList<DocumentEmail>> GetQueuedAsync(int batchSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// The queue-depth figures for one clinic — <see cref="DocumentEmailOutboxDepth"/>. Scoped by clinic, unlike
    /// <see cref="GetQueuedAsync"/>: the dispatcher legitimately crosses clinics, an operator read must not.
    /// </summary>
    Task<DocumentEmailOutboxDepth> GetOutboxDepthAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    Task AddAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default);
    Task UpdateAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default);
}
