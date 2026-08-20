namespace ClinicManagement.API.Models;

/// <summary>
/// Resetting one clinic account's second factor from the vendor console
/// (<c>hosted-security-hardening</c> FR-1.4).
///
/// <para>⚠️ The cabinet is <b>not</b> on the wire — it is the route — so a mis-keyed address can only reach an
/// account at the practice the vendor already has open, rather than any account in the deployment.</para>
/// </summary>
public class ResetClinicUserSecondFactorRequest
{
    /// <summary>
    /// The address of the account to disarm, as given by the person on the telephone. Mandatory; the handler
    /// refuses a blank one in French, so the refusal has one wording and one place.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Mandatory. Unlike a suspension's motif — which lives on the entitlement — this one has <b>no domain row to
    /// live on</b>, because clearing a factor leaves no trace behind: it is written to the console's own journal
    /// and that row is the whole record of the operation.
    /// </summary>
    public string? Reason { get; set; }
}
