using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class DeviceRegistrationRepository : IDeviceRegistrationRepository
{
    private readonly ApplicationDbContext _context;

    public DeviceRegistrationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DeviceRegistration?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.DeviceRegistrations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    /// <summary>
    /// The one deliberately unfiltered read here — see the interface for why the alternative is a 500 rather than
    /// better isolation.
    /// </summary>
    public async Task<DeviceRegistration?> GetByTokenAcrossClinicsAsync(
        string token, CancellationToken cancellationToken = default) =>
        await _context.DeviceRegistrations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Token == token, cancellationToken);

    public async Task<IReadOnlyList<DeviceRegistration>> GetActiveForUsersAsync(
        Guid clinicId, IEnumerable<string> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            // No audience, so no query. An empty `Contains` translates to `WHERE false` on PostgreSQL, which is
            // harmless but is still a round trip for an answer already known.
            return Array.Empty<DeviceRegistration>();
        }

        return await _context.DeviceRegistrations
            .Where(d => d.ClinicId == clinicId && d.IsActive && ids.Contains(d.UserId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceRegistration>> GetActiveForUserAsync(
        Guid clinicId, string userId, CancellationToken cancellationToken = default) =>
        await _context.DeviceRegistrations
            .Where(d => d.ClinicId == clinicId && d.UserId == userId && d.IsActive)
            .OrderByDescending(d => d.LastSeenAt)
            .ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);

    public async Task<int> CountActiveAsync(
        Guid clinicId, DevicePlatform platform, CancellationToken cancellationToken = default) =>
        await _context.DeviceRegistrations
            .CountAsync(d => d.ClinicId == clinicId && d.Platform == platform && d.IsActive, cancellationToken);

    public async Task AddAsync(DeviceRegistration registration, CancellationToken cancellationToken = default) =>
        await _context.DeviceRegistrations.AddAsync(registration, cancellationToken);

    public Task UpdateAsync(DeviceRegistration registration, CancellationToken cancellationToken = default)
    {
        _context.DeviceRegistrations.Update(registration);
        return Task.CompletedTask;
    }
}
