using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>Reads the dashboard's « Activité » section for one clinic over one period.</summary>
public interface IDashboardActivityReader
{
    Task<DashboardActivityDto> ReadAsync(Guid clinicId, DashboardPeriod period, CancellationToken cancellationToken);
}
