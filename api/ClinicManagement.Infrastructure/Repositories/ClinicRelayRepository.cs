using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>The PC de secours rows. Writes are staged; the caller's unit of work commits.</summary>
public class ClinicRelayRepository : IClinicRelayRepository
{
    private readonly ApplicationDbContext _context;

    public ClinicRelayRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<ClinicRelay?> GetCurrentForClinicAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        _context.ClinicRelays
            .Where(r => r.ClinicId == clinicId && r.Status != ClinicRelayStatus.Retired)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ClinicRelay?> GetLatestForClinicAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        _context.ClinicRelays
            .Where(r => r.ClinicId == clinicId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ClinicRelay?> GetByIdAcrossClinicsAsync(Guid relayId, CancellationToken cancellationToken = default) =>
        _context.ClinicRelays.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == relayId, cancellationToken);

    public Task<ClinicRelay?> FindByPairingCodeAcrossClinicsAsync(
        string codeHash, CancellationToken cancellationToken = default) =>
        _context.ClinicRelays.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.PairingCodeHash == codeHash, cancellationToken);

    public async Task<IReadOnlyList<ClinicRelay>> GetLiveAsync(CancellationToken cancellationToken = default) =>
        await _context.ClinicRelays
            .Where(r => r.Status != ClinicRelayStatus.Retired)
            .OrderBy(r => r.ClinicId)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ClinicRelay relay, CancellationToken cancellationToken = default) =>
        await _context.ClinicRelays.AddAsync(relay, cancellationToken);
}
