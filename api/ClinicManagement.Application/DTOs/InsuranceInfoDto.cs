namespace ClinicManagement.Application.DTOs;

/// <summary>
/// A patient's private insurance, on the wire. Serves as both the request and the response shape.
///
/// <para>⚠️ <b>Both halves are nullable (AC-21)</b>, mirroring <c>InsuranceInfo</c>: a patient can name their
/// insurer with the card at home, or hand over a policy number on a slip that does not name the company. They
/// used to be non-nullable <c>string.Empty</c>, which is what let the client pad a missing half with the literal
/// <c>"Unknown"</c> — a value no later read could tell from a real insurer's name. Sending a block with
/// <b>neither</b> side is how a caller says « clear it »; the value object refuses to be constructed from one.</para>
/// </summary>
public class InsuranceInfoDto
{
    public string? Provider { get; set; }
    public string? PolicyNumber { get; set; }
    public string? GroupNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
}
