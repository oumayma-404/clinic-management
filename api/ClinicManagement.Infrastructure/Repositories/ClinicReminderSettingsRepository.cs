using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class ClinicReminderSettingsRepository : IClinicReminderSettingsRepository
{
    private readonly ApplicationDbContext _context;

    public ClinicReminderSettingsRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    // Keyed by clinic id (shared primary key). Read cross-clinic-safe: the dispatcher runs with no clinic
    // scope, so the global query filter is inactive and it can resolve any row's clinic by id.
    public async Task<ClinicReminderSettings?> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.ClinicReminderSettings
            .FirstOrDefaultAsync(s => s.Id == clinicId, cancellationToken);
    }

    public async Task AddAsync(ClinicReminderSettings settings, CancellationToken cancellationToken = default)
    {
        await _context.ClinicReminderSettings.AddAsync(settings, cancellationToken);
    }

    public Task UpdateAsync(ClinicReminderSettings settings, CancellationToken cancellationToken = default)
    {
        _context.ClinicReminderSettings.Update(settings);
        return Task.CompletedTask;
    }
}
