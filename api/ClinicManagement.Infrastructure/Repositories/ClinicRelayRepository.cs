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

    public async Task<IReadOnlyDictionary<Guid, ClinicRelay>> GetLatestForClinicsAsync(
        IEnumerable<Guid> clinicIds, CancellationToken cancellationToken = default)
    {
        var ids = clinicIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ClinicRelay>();
        }

        // A cabinet holds a handful of rows (one per setup attempt), so the choice of the newest is made here rather
        // than in SQL — in the same order GetLatestForClinicAsync uses, so the console and the card name the same PC.
        var rows = await _context.ClinicRelays
            .Where(r => ids.Contains(r.ClinicId))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ClinicId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).First());
    }

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

    // A coffre file never reached the cloud, so the PC cannot copy it: hosted rows only.
    public async Task<long> GetHostedFileBytesAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.PatientFiles
            .Where(f => f.ClinicId == clinicId && f.Residency == FileResidency.Hosted)
            .SumAsync(f => (long?)f.FileSize, cancellationToken) ?? 0;

    public async Task AddAsync(ClinicRelay relay, CancellationToken cancellationToken = default) =>
        await _context.ClinicRelays.AddAsync(relay, cancellationToken);
}
