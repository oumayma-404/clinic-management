using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class AttendanceTerminalRepository : IAttendanceTerminalRepository
{
    private readonly ApplicationDbContext _context;

    public AttendanceTerminalRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AttendanceTerminal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceTerminals.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    // IgnoreQueryFilters: the borne's request has no user, so no clinic is in scope until this read names one.
    public async Task<AttendanceTerminal?> FindActiveBySecretHashAcrossClinicsAsync(
        string secretHash, CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceTerminals
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.SecretHash == secretHash && t.RevokedAt == null, cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceTerminal>> ListAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceTerminals
            .Where(t => t.ClinicId == clinicId)
            .OrderBy(t => t.RevokedAt == null ? 0 : 1)
            .ThenByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(AttendanceTerminal terminal, CancellationToken cancellationToken = default)
    {
        await _context.AttendanceTerminals.AddAsync(terminal, cancellationToken);
    }
}
