namespace ClinicManagement.API.Models;

/// <summary>
/// Resetting one clinic account's password from the vendor console.
///
/// <para>⚠️ The cabinet is <b>not</b> on the wire — it is the route — so a mis-keyed address can only reach an
/// account at the practice the vendor already has open, rather than any account in the deployment. Its
/// second-factor sibling is shaped the same way and for the same reason.</para>
/// </summary>
public class ResetClinicUserPasswordRequest
{
    /// <summary>
    /// The address of the account to re-credential, as given by the person on the telephone. Mandatory; the handler
    /// refuses a blank one in French, so the refusal has one wording and one place.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Mandatory. Like the second factor's motif and unlike a suspension's, this one has <b>no domain row to live
    /// on</b>: <c>User.SetPassword</c> records neither who called it nor why, so the console's own journal row is
    /// the whole record of the operation.
    /// </summary>
    public string? Reason { get; set; }
}
