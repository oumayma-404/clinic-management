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
        int batchSize, CancellationToken cancellationToken = default) =>
        await _context.DocumentEmails
            .Where(e => e.Status == DocumentEmailStatus.Queued)
            .OrderBy(e => e.QueuedAt)
            .ThenBy(e => e.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default) =>
        await _context.DocumentEmails.AddAsync(documentEmail, cancellationToken);

    public Task UpdateAsync(DocumentEmail documentEmail, CancellationToken cancellationToken = default)
    {
        _context.DocumentEmails.Update(documentEmail);
        return Task.CompletedTask;
    }
}
