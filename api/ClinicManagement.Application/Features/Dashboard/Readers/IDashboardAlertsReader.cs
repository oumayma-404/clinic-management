using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// Reads the dashboard's « À traiter » section — standing state across the clinic's operational subsystems. All
/// point-in-time, so it takes no <see cref="DashboardPeriod"/>: a waiting room is not a monthly total.
/// </summary>
public interface IDashboardAlertsReader
{
    Task<DashboardAlertsDto> ReadAsync(Guid clinicId, DateTime nowUtc, CancellationToken cancellationToken);
}
