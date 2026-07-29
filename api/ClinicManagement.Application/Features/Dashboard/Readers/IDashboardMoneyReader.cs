using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// Reads the dashboard's « Argent » section plus the point-in-time créances total. The two are produced together
/// because both need the clinic's billed-plan set, and computing it twice is both wasteful and a chance for the
/// cash and debt sides to de-duplicate differently.
/// </summary>
public interface IDashboardMoneyReader
{
    Task<(DashboardMoneyDto Money, DashboardReceivablesDto Receivables)> ReadAsync(
        Guid clinicId, DashboardPeriod period, DateTime nowUtc, CancellationToken cancellationToken);
}
