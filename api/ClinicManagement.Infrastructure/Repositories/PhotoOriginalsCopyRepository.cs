using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class PhotoOriginalsCopyRepository : IPhotoOriginalsCopyRepository
{
    private readonly ApplicationDbContext _context;

    public PhotoOriginalsCopyRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<PhotoOriginalsCopy?> GetAsync(Guid clinicId, string heldBy, CancellationToken cancellationToken = default) =>
        _context.PhotoOriginalsCopies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.ClinicId == clinicId && c.HeldBy == heldBy, cancellationToken);

    // `IgnoreQueryFilters` + an explicit ClinicId for GetVaultTotalsAsync's reason: the daily pass reads this under
    // UseSystemWide, and the parameter is the authoritative check.
    public async Task<IReadOnlyList<PhotoOriginalsCopy>> ListAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.PhotoOriginalsCopies
            .IgnoreQueryFilters()
            .Where(c => c.ClinicId == clinicId)
            .OrderBy(c => c.HeldBy)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(PhotoOriginalsCopy copy, CancellationToken cancellationToken = default) =>
        await _context.PhotoOriginalsCopies.AddAsync(copy, cancellationToken);
}
