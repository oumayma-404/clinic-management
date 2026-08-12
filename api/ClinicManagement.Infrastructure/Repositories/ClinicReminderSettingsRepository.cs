using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
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

    // The one IgnoreQueryFilters() read of this class, and the interface says why: the webhook that needs it is
    // anonymous, so it has no clinic in scope — the row it is looking for is what tells it whose the payload is.
    public async Task<ClinicReminderSettings?> GetByWhatsAppBusinessAccountIdAsync(
        string businessAccountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(businessAccountId))
        {
            return null;
        }

        var id = businessAccountId.Trim();
        return await _context.ClinicReminderSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.WhatsAppBusinessAccountId == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicReminderSettings>> GetAwaitingTemplateReviewAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ClinicReminderSettings
            .Where(s => s.WhatsAppConnectionStatus == WhatsAppConnectionStatus.Connected
                        && s.WhatsAppBusinessAccountId != null
                        && (s.WhatsAppTemplateStatus == null
                            || WhatsAppTemplateStatuses.AwaitingMeta.Contains(s.WhatsAppTemplateStatus.Value)))
            .OrderBy(s => s.WhatsAppTemplateStatusCheckedAtUtc)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ClinicReminderSettings settings, CancellationToken cancellationToken = default)
    {
        await _context.ClinicReminderSettings.AddAsync(settings, cancellationToken);
    }

    public Task UpdateAsync(ClinicReminderSettings settings, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(settings);
        if (entry.State == EntityState.Detached)
        {
            _context.ClinicReminderSettings.Update(settings);
        }
        return Task.CompletedTask;
    }
}
