using System.Globalization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="IDemoPolicy"/> over the resolved <see cref="DeploymentProfile"/>. The two capabilities read no
/// configuration (no key may turn a real deployment read-only or open an anonymous door); only the lifetime does.
/// </summary>
public class DemoPolicy : IDemoPolicy
{
    public const string SessionMinutesKey = "Demo:SessionMinutes";

    public const int DefaultSessionMinutes = 120;

    private const int MaxSessionMinutes = 24 * 60;

    private readonly DeploymentProfile _profile;

    public DemoPolicy(DeploymentProfile profile, IConfiguration configuration)
    {
        _profile = profile;
        SessionLifetime = TimeSpan.FromMinutes(ReadMinutes(configuration[SessionMinutesKey]));
    }

    public bool RefusesWrites => _profile.RefusesWrites;

    public bool IssuesDemoSessions => _profile.IssuesDemoSessions;

    public bool RollsDashboardPeriods => _profile.IssuesDemoSessions;

    public TimeSpan SessionLifetime { get; }

    // Hand-parsed: GetValue<int> throws on a typo, and that would stop the demo from starting.
    private static int ReadMinutes(string? configured) =>
        int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
        && minutes is > 0 and <= MaxSessionMinutes
            ? minutes
            : DefaultSessionMinutes;
}
