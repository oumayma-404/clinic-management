using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Deployment;

/// <summary>
/// Reads <see cref="DeploymentProfile.RequiresAdminSecondFactor"/> and nothing else
/// (<c>hosted-security-hardening</c> FR-1.1), on <c>SubscriptionPolicy</c>'s pattern.
///
/// <para>There is no configuration key here on purpose — see the interface's own note.</para>
/// </summary>
public class SecondFactorPolicy : ISecondFactorPolicy
{
    private readonly DeploymentProfile _profile;

    public SecondFactorPolicy(DeploymentProfile profile)
    {
        _profile = profile;
    }

    public bool RequiresAdminSecondFactor => _profile.RequiresAdminSecondFactor;
}
