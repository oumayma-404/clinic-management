namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Whether this deployment requires a clinic <b>administrator</b> to hold a second factor
/// (<c>hosted-security-hardening</c> FR-1.1).
///
/// <para>⚠️ <b>A seam because it is structurally required, not for style.</b> <c>DeploymentProfile</c> lives in
/// Infrastructure and this project references Domain alone, so no Application type can name it —
/// <c>ISubscriptionPolicy</c> and <c>IOsPushAvailability</c> exist for exactly the same reason.</para>
///
/// <para>⚠️ <b>It reads no configuration key</b>, and there is deliberately no <c>Auth:RequireSecondFactor</c> to
/// find: the answer is derived from the deployment's <i>kind</i>, so a clinic's own Windows PC cannot be one
/// config edit away from locking its only administrator out of its own patient records. That is
/// <c>DeploymentProfile</c>'s own invariant, and this seam preserves it by carrying no setting.</para>
///
/// <para>⚠️ <b>« Required of admins » is not « enrolment is available ».</b> A doctor or secretary on <i>any</i>
/// deployment may enrol voluntarily from « Sécurité », and a volunteer who has enrolled is asked for their code
/// wherever they sign in. What this decides is whether an administrator is <i>refused a session without one</i> —
/// and, consequently, whether such an administrator may disable theirs.</para>
/// </summary>
public interface ISecondFactorPolicy
{
    bool RequiresAdminSecondFactor { get; }
}
