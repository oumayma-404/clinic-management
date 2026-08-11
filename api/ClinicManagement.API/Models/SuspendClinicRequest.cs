namespace ClinicManagement.API.Models;

/// <summary>
/// Suspending a cabinet from the vendor console (<c>platform-console</c> AC-6.1).
///
/// <para>⚠️ There is <b>no <c>suspend</c> flag on the wire</b>: the direction is the route, so a body a client
/// truncated or forgot cannot turn « suspendre » into « lever ». Lifting carries no body at all.</para>
/// </summary>
public class SuspendClinicRequest
{
    /// <summary>Mandatory. The handler refuses a blank one in French — nothing here validates it, so the refusal has
    /// one wording and one place.</summary>
    public string? Reason { get; set; }
}
