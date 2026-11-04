namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// What the public demo (<c>demo.apexa.tn</c>) does differently, for code that cannot name <c>DeploymentProfile</c>
/// (it lives in Infrastructure; this project references Domain alone — <see cref="ISubscriptionPolicy"/>'s reason).
/// </summary>
public interface IDemoPolicy
{
    /// <summary>Every request that would change stored data is refused with <c>demo_read_only</c>.</summary>
    bool RefusesWrites { get; }

    /// <summary>An anonymous visitor may open a session as the demo dentist.</summary>
    bool IssuesDemoSessions { get; }

    /// <summary>The dashboard reads the last 7 / 30 days, so a cabinet rebuilt overnight never shows an empty month.</summary>
    bool RollsDashboardPeriods { get; }

    /// <summary>The most a demo session may last from its first minute, whatever it refreshes (spec FR-3).</summary>
    TimeSpan SessionLifetime { get; }
}
