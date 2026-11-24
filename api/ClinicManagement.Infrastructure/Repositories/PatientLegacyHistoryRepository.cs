using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class PatientLegacyHistoryRepository : IPatientLegacyHistoryRepository
{
    private readonly ApplicationDbContext _context;

    public PatientLegacyHistoryRepository(ApplicationDbContext context) => _context = context;

    public async Task<IReadOnlyList<PatientLegacyHistoryEntry>> GetByPatientAsync(
        Guid patientId, CancellationToken cancellationToken = default)
    {
        // `Date == null` first in the key so undated lines land LAST under a descending date; `Id` last so two
        // lines with the same day and position never swap between renders.
        return await _context.PatientLegacyHistoryEntries
            .AsNoTracking()
            .Where(e => e.PatientId == patientId)
            .OrderBy(e => e.Date == null)
            .ThenByDescending(e => e.Date)
            .ThenBy(e => e.Position)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<PatientLegacyHistoryEntry> entries, CancellationToken cancellationToken = default)
    {
        await _context.PatientLegacyHistoryEntries.AddRangeAsync(entries, cancellationToken);
    }
}
