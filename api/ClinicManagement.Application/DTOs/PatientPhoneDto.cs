namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One of a patient's additional phone numbers, as a screen reads it — see
/// <see cref="Domain.ValueObjects.PatientPhone"/>.
/// </summary>
public class PatientPhoneDto
{
    /// <summary>The number as reception typed it. This is what is DISPLAYED.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// The dialable form. This is what a <c>tel:</c> link and a WhatsApp action use — never
    /// <see cref="Value"/>, which keeps its spaces and carries no country code for a foreign number.
    /// <para>Never null, unlike <c>PatientDto.PhoneE164</c>: the entity refuses a number that has none.</para>
    /// </summary>
    public string E164 { get; set; } = string.Empty;
}

/// <summary>
/// One additional number as a WRITE request carries it — the create and update commands.
///
/// <para>⚠️ Separate from <see cref="PatientPhoneDto"/> because the two shapes genuinely differ: a write
/// carries the country selector's <see cref="Region"/> and no E.164 (the server resolves it), a read carries
/// the E.164 and no region (nothing stores one — see <c>UpdatePatientCommand.PhoneRegion</c>). Sharing one
/// class would mean two fields that are each ignored on one side, which is how a client comes to send an
/// E.164 the server silently drops.</para>
/// </summary>
public class PatientPhoneInputDto
{
    /// <summary>The number as typed.</summary>
    public string? Value { get; set; }

    /// <summary>
    /// The country to read <see cref="Value"/> as when it carries no country code — this row's own country
    /// selector. ⚠️ Per row, not per patient: a patient's mobile may be Tunisian and their child's French.
    /// </summary>
    public string? Region { get; set; }

}
