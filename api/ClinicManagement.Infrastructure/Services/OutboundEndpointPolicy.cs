using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Deployment;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Answers <see cref="IOutboundEndpointPolicy"/> from the resolved <see cref="DeploymentProfile"/>.
///
/// <para>
/// Private endpoints are permitted on <see cref="DeploymentKind.SelfHostedLan"/> alone — the one topology where
/// the clinic owns the network the server sits on, so a relay at <c>192.168.x.x</c> is the practice's own kit.
/// Both hosted kinds refuse: there the private range belongs to the operator, and a tenant naming an address in
/// it is aiming the server at the operator's own infrastructure.
/// </para>
///
/// <para>
/// ⚠️ Deliberately a <c>switch</c> over the kind with no <c>_ =&gt;</c> catch-all permitting anything: a fourth
/// deployment kind added later must state its own answer, and the compiler's default for an unlisted case here is
/// the safe direction.
/// </para>
/// </summary>
public class OutboundEndpointPolicy : IOutboundEndpointPolicy
{
    private readonly DeploymentProfile _profile;

    public OutboundEndpointPolicy(DeploymentProfile profile)
    {
        _profile = profile;
    }

    public bool AllowsPrivateNetworkEndpoints => _profile.Kind switch
    {
        DeploymentKind.SelfHostedLan => true,
        _ => false,
    };
}
