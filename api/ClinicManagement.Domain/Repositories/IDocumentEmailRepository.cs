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
///
/// <para>⚠️ <see cref="Blocked"/> is what makes <see cref="Queued"/> readable (review finding 5). Rows whose clinic
/// cannot send are parked rather than left in the scan, and without the count a growing queue with an ancient
/// oldest row is indistinguishable from « the dispatcher is not running » — the exact confusion this endpoint
/// exists to remove.</para>
/// </summary>
public record DocumentEmailOutboxDepth(int Queued, int Blocked, int Failed, DateTime? OldestQueuedAt);

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
    /// Queued rows due for a dispatch attempt, oldest first and <b>bounded twice</b> — by
    /// <paramref name="batchSize"/> for the tick, and by <paramref name="perClinicBound"/> for any one clinic's
    /// share of it. Crosses clinics deliberately (the job runs with no clinic in scope, so the global filter is
    /// inactive).
    ///
    /// <para>⚠️ The per-clinic bound is not symmetry with the reminder outbox for its own sake (review finding 5).
    /// Without it the scan is « queued, oldest first, take N » across every clinic, so one practice's backlog — or,
    /// before <c>Blocked</c> existed, one practice's unsendable rows sitting at the front — consumed every minutely
    /// tick and stopped « Envoyer par email » for all the others, while the job logged a clean run.</para>
    /// </summary>
    Task<IReadOnlyList<DocumentEmail>> GetQueuedAsync(
        int batchSize, int perClinicBound, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parked rows to re-examine, oldest first. The half that keeps <c>DocumentEmailStatus.Blocked</c> from being a
    /// one-way door: the dispatcher re-resolves each row's clinic settings and returns the sendable ones to the
    /// queue. Crosses clinics for the same reason as the scan above.
    /// </summary>
    Task<IReadOnlyList<DocumentEmail>> GetBlockedForReviewAsync(
        int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// The queue-depth figures for one clinic — <see cref="DocumentEmailOutboxDepth"/>. Scoped by clinic, unlike
    /// <see cref="GetQueuedAsync"/>: the dispatcher legitimately crosses clinics, an operator read must not.
    /// </summary>
    Task<DocumentEmailOutboxDepth> GetOutboxDepthAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    Task AddAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default);
    Task UpdateAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default);
}
