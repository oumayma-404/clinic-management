using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// Reads the « Tendance » series — collected cash per clinic-local month. Independent of the selected period: a
/// six-month trend is not derivable from a one-day window, so it takes the period only to reach its
/// <see cref="DashboardPeriod.TrendWindow"/>.
/// </summary>
public interface IDashboardTrendReader
{
    Task<List<MonthlyCollectedPointDto>> ReadAsync(
        Guid clinicId, DashboardPeriod period, DateTime nowUtc, CancellationToken cancellationToken);
}
