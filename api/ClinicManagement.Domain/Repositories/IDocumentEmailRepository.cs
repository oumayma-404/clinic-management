using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

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

    Task AddAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default);
    Task UpdateAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default);
}
