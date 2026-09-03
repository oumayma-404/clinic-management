using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// ⚠️ <b>No <c>IgnoreQueryFilters()</c> on the request-scoped reads, and none is wanted.</b>
/// <see cref="FileUploadSession"/> carries a non-nullable <c>ClinicId</c> and is filtered, so a caller with no
/// clinic in scope reads nothing rather than another practice's in-flight upload — which is what makes a
/// forgotten scope loud instead of a cross-tenant read.
/// </summary>
public class FileUploadSessionRepository : IFileUploadSessionRepository
{
    private readonly ApplicationDbContext _context;

    public FileUploadSessionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<FileUploadSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.FileUploadSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task AddAsync(FileUploadSession session, CancellationToken cancellationToken = default) =>
        await _context.FileUploadSessions.AddAsync(session, cancellationToken);

    public Task UpdateAsync(FileUploadSession session, CancellationToken cancellationToken = default)
    {
        // The guarded form the other repositories use: Version maps onto xmin, so a blind Update on a detached
        // instance sends `WHERE xmin = 0`, matches nothing, and 409s with nobody at fault.
        if (_context.Entry(session).State == EntityState.Detached)
        {
            _context.FileUploadSessions.Update(session);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(FileUploadSession session, CancellationToken cancellationToken = default)
    {
        _context.FileUploadSessions.Remove(session);
        return Task.CompletedTask;
    }

    /// <summary>
    /// ⚠️ <b><c>IgnoreQueryFilters()</c> here, and only here.</b> The sweep runs with no request behind it, so it
    /// declares <c>UseSystemWide</c> — but a system-wide scope is exactly what the filter honours, and reading
    /// across clinics is the whole job: one expired upload belongs to one cabinet and the sweep serves them all.
    /// It selects nothing but what it is about to reclaim.
    /// </summary>
    public async Task<IReadOnlyList<FileUploadSession>> GetExpiredAsync(
        DateTime nowUtc, int max, CancellationToken cancellationToken = default) =>
        await _context.FileUploadSessions
            .IgnoreQueryFilters()
            .Where(s => s.ExpiresAtUtc <= nowUtc)
            .OrderBy(s => s.ExpiresAtUtc)
            .ThenBy(s => s.Id)
            .Take(max)
            .ToListAsync(cancellationToken);
}
