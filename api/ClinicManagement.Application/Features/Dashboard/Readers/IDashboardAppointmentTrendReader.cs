using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// Reads « Rendez-vous — 6 derniers mois »: appointment counts per clinic-local month, oldest first.
/// </summary>
public interface IDashboardAppointmentTrendReader
{
    Task<List<MonthlyAppointmentPointDto>> ReadAsync(
        Guid clinicId, DashboardPeriod period, DateTime nowUtc, CancellationToken cancellationToken);
}
