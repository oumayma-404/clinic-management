namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Whether a clinic may point an integration endpoint (SMS gateway, WhatsApp API, SMTP host) at a
/// <b>private-network</b> address.
///
/// <para>
/// An Application-side seam for one deployment capability, on <c>IPublicAppUrlProvider</c>'s pattern and for the
/// same reason: this project references no configuration package and knows nothing of
/// <c>Deployment:Profile</c>, but the rule it needs is a deployment question.
/// </para>
///
/// <para>
/// ⚠️ <b>The answer turns on who the tenant is.</b> On a clinic's own PC the tenant and the operator are the same
/// person, and a relay on the practice's LAN is an ordinary thing to configure. On a hosted backend they are
/// strangers: the private range reachable from the API container is the <i>operator's</i> infrastructure — the
/// database, the object store, the loopback the Hangfire dashboard trusts — so a tenant naming an address in it
/// is pointing the server at its own insides. Default <b>false</b>: a new profile that forgets to answer refuses
/// rather than permits.
/// </para>
/// </summary>
public interface IOutboundEndpointPolicy
{
    /// <summary>True only where a private endpoint is the clinic's own network rather than the operator's.</summary>
    bool AllowsPrivateNetworkEndpoints { get; }
}
