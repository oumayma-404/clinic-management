using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="ISubscriptionPolicy"/> over the resolved <see cref="DeploymentProfile"/> and the
/// <c>Subscription</c> configuration section.
///
/// <para>⚠️ <b><see cref="RequiresSubscription"/> reads no configuration key at all</b> (AC-7.3). There is
/// deliberately no <c>Subscription:Enabled</c> to find: enforcement is a property of the deployment's kind, and a
/// key that could flip it would put a clinic's own Windows PC one config edit away from refusing its own patient
/// records. <see cref="TrialDays"/> is the operator's, and <c>SubscriptionProvisioningTests</c> pins that no
/// <c>Subscription:*</c> value moves the first answer.</para>
/// </summary>
public class SubscriptionPolicy : ISubscriptionPolicy
{
    /// <summary>
    /// Public so the migration's grandfathering note and the trial's own default cannot disagree with the setting's
    /// fallback — the scaffolded-default trap `backup-schedule-backfill` exists to catch.
    /// </summary>
    public const int DefaultTrialDays = 30;

    /// <summary>A guard on the operator's value, not a policy: a year of free days is a typo, and 0 disables the trial.</summary>
    private const int MaxTrialDays = 365;

    private readonly DeploymentProfile _profile;
    private readonly IConfiguration _configuration;

    public SubscriptionPolicy(DeploymentProfile profile, IConfiguration configuration)
    {
        _profile = profile;
        _configuration = configuration;
    }

    public bool RequiresSubscription => _profile.RequiresSubscription;

    /// <summary>
    /// ⚠️ <b>Parsed by hand rather than through <c>GetValue&lt;int?&gt;</c>, which <i>throws</i> on a value it cannot
    /// convert.</b> This is read while a cabinet is being provisioned, so a typo in the operator's config would
    /// abort clinic creation with a binder exception instead of falling back — the « a mistyped setting must refuse
    /// nothing, never everything » rule <c>ClientVersionMiddleware</c> already follows. Anything unreadable, absent
    /// or out of range reads as the default.
    /// </summary>
    public int TrialDays =>
        int.TryParse(_configuration["Subscription:TrialDays"], out var configured)
        && configured is > 0 and <= MaxTrialDays
            ? configured
            : DefaultTrialDays;
}
