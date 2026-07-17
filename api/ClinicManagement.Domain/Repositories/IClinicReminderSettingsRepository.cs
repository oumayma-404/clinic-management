using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for a clinic's per-clinic reminder settings (1:1 with the clinic; keyed by clinic id).
/// Mutations only stage changes — the caller commits via <c>IUnitOfWork</c>.
/// </summary>
public interface IClinicReminderSettingsRepository
{
    Task<ClinicReminderSettings?> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task AddAsync(ClinicReminderSettings settings, CancellationToken cancellationToken = default);
    Task UpdateAsync(ClinicReminderSettings settings, CancellationToken cancellationToken = default);
}
