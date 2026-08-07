namespace ClinicManagement.Application.Features.Dashboard;

/// <summary>
/// The windows the dashboard can be read over. A closed enum rather than a string so an unknown value is a
/// binding failure at the edge instead of a silently-empty period deep inside the readers.
/// </summary>
public enum DashboardPeriodKey
{
    /// <summary>The current clinic-local day.</summary>
    Today = 0,

    /// <summary>The current clinic-local week, Monday-based (matching the agenda's <c>weekStartsOn: 1</c>).</summary>
    Week = 1,

    /// <summary>The current clinic-local calendar month. The dashboard's default.</summary>
    Month = 2
}
