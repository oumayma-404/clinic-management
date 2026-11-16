using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>The one column the upload path needs of a cabinet. <c>Clinics</c> carries no tenant filter.</summary>
public sealed class ClinicStorageQuotaReader : IClinicStorageQuotaReader
{
    private readonly ApplicationDbContext _context;

    public ClinicStorageQuotaReader(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<long?> GetOverrideAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        _context.Clinics
            .AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => c.StorageQuotaBytes)
            .FirstOrDefaultAsync(cancellationToken);
}
