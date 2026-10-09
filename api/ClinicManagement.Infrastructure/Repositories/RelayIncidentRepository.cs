using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>The vendor's PC de secours episodes. Writes are staged; the caller's unit of work commits.</summary>
public class RelayIncidentRepository : IRelayIncidentRepository
{
    private readonly ApplicationDbContext _context;

    public RelayIncidentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<RelayIncident>> GetOpenAsync(CancellationToken cancellationToken = default) =>
        await _context.RelayIncidents
            .Where(i => i.EndedAtUtc == null)
            .OrderBy(i => i.ClinicId)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(RelayIncident incident, CancellationToken cancellationToken = default) =>
        await _context.RelayIncidents.AddAsync(incident, cancellationToken);
}
