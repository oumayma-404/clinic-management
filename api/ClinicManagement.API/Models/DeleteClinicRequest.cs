namespace ClinicManagement.API.Models;

/// <summary>
/// What the console sends to delete a cabinet (<c>clinic-account-removal</c>). Both fields are mandatory and both
/// are refused in French by the handler, so each refusal has one wording and one place.
/// </summary>
public class DeleteClinicRequest
{
    /// <summary>
    /// What the vendor typed to confirm: <b>one of the cabinet's account addresses</b> exactly as the panel shows
    /// it, or its name where the cabinet has no account. Compared server-side against the cabinet the URL's id
    /// resolved to — the failure being caught is a wrong row in the portfolio, which a client comparing two of its
    /// own strings cannot see.
    ///
    /// <para>⚠️ An address and not the name, because <c>Clinic.Name</c> is not unique: two cabinets may share one,
    /// and a typed name cannot tell them apart.</para>
    /// </summary>
    public string? Confirmation { get; set; }

    /// <summary>
    /// Why. It lands on the journal row, which is the only thing that outlives the cabinet — and it is what
    /// separates « cabinet de test » from « le cabinet a demandé la suppression de ses données » a year later.
    /// </summary>
    public string? Reason { get; set; }
}
