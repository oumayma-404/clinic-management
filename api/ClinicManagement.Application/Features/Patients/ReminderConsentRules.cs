using System.Linq;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Turns the wire's consent string into the enum — <see cref="DentitionRules"/>' twin, and here for the same
/// reason.
///
/// <para>⚠️ <b>This API registers no <c>JsonStringEnumConverter</c>.</b> A raw enum property on a DTO therefore
/// travels as <c>0</c>/<c>1</c>/<c>2</c>, and a client sending <c>"Refused"</c> is answered with a 400 by the
/// model binder — before any handler runs, so no French message and no log line. The consent field shipped that
/// way and the browser silently showed « non renseigné » over every stored answer; nothing in <c>tsc</c>, the
/// unit suite or <c>check:responsive</c> could see it.</para>
/// </summary>
public static class ReminderConsentRules
{
    /// <summary>
    /// Parses the wire value. Returns <b>null</b> for an absent or unrecognised string, which every caller
    /// reads as « leave the stored answer alone ».
    ///
    /// <para>⚠️ Unrecognised is null rather than <see cref="PatientReminderConsent.NotRecorded"/>, and the
    /// difference matters: a typo must not silently <i>erase</i> a refusal the patient gave. Un-recording an
    /// answer requires sending <c>"NotRecorded"</c> deliberately.</para>
    ///
    /// <para>⚠️ A <b>numeric</b> string is refused as well. <c>Enum.TryParse</c> accepts <c>"2"</c> happily,
    /// which would leave the wire with two spellings for one answer — and a client still sending the integer is
    /// a client that has not been told the shape changed, which should be loud rather than quietly accepted.</para>
    /// </summary>
    public static PatientReminderConsent? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().All(char.IsAsciiDigit))
        {
            return null;
        }

        return Enum.TryParse<PatientReminderConsent>(value, ignoreCase: true, out var parsed)
               && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }
}
