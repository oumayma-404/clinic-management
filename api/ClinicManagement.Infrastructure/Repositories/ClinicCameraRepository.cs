using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class ClinicCameraRepository : IClinicCameraRepository
{
    private readonly ApplicationDbContext _context;

    public ClinicCameraRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<ClinicCamera?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.ClinicCameras.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<ClinicCamera?> GetByFingerprintAsync(
        Guid clinicId,
        string fingerprint,
        CancellationToken cancellationToken = default) =>
        _context.ClinicCameras.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Fingerprint == fingerprint,
            cancellationToken);

    public async Task<IReadOnlyList<ClinicCamera>> ListAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.ClinicCameras
            .Where(c => c.ClinicId == clinicId)
            .OrderBy(c => c.Label)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(ClinicCamera camera, CancellationToken cancellationToken = default) =>
        await _context.ClinicCameras.AddAsync(camera, cancellationToken);

    public Task DeleteAsync(ClinicCamera camera, CancellationToken cancellationToken = default)
    {
        _context.ClinicCameras.Remove(camera);
        return Task.CompletedTask;
    }
}
