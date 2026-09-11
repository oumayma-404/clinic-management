namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// The one refusal a phone number can meet, written once (international-phone-numbers AC-5).
///
/// <para><b>One authority, because three copies is how the sentence stopped being true.</b> The same literal
/// « Utilisez un numéro tunisien à 8 chiffres (ou +216…) » was pasted into <c>PatientFromRequest</c>,
/// <c>UpdatePatientCommand</c> and <c>PatientImportRowReader</c>. The rule now accepts every country, so all
/// three were wrong at once — and the import's copy is the only place a user ever learns the expected format,
/// because a rejected import row has no in-app edit affordance and the sentence <i>is</i> the fix instruction.</para>
///
/// <para><b>It names no country.</b> The old one did, which is what made it a lie the moment the rule widened.
/// This one names the two ways out instead — pick the country, or write the number the way it is written
/// internationally — so it stays true whatever countries the product later sells into.</para>
///
/// <para>⚠️ <b>No <c>Result.Code</c> here, deliberately.</b> Per <c>Result.Code</c>'s own docstring a code is for
/// a caller that <i>branches</i>, and nothing branches on this: every consumer shows the sentence to the user.
/// An unused code is a contract nobody is honouring.</para>
///
/// <para>⚠️ <b><see cref="Invalid"/> and the browser's <c>PHONE_ERROR_FR</c> are the SAME SENTENCE, word for
/// word.</b> This docstring used to claim the opposite — « a separate, client-owned string … not a mirror of
/// this one, and it does not restate it » — and so did <c>phone.ts</c>'s. Both were false, and the cost was not
/// cosmetic: when the server refused a number the client had already accepted, the refusal on screen was
/// indistinguishable from the client's own pre-check, so the bug read as « the country selector does nothing »
/// and took a production report plus a source-level investigation to place (2026-09-11, the region that never
/// left the browser — see <c>CreatePatientCommand.PhoneRegion</c>). If the two are ever made to differ, say so
/// here <i>and</i> there; while they are identical, neither side may be described as distinct from the
/// other.</para>
/// </summary>
public static class PhoneRefusals
{
    /// <summary>
    /// A number no country can parse, as a patient's own number. Used where the number is refused outright.
    /// </summary>
    public const string Invalid =
        "Numéro de téléphone invalide. Choisissez le pays, "
        + "ou saisissez le numéro au format international (+33…).";

    /// <summary>
    /// The same refusal for one row of an import, quoting the offending value so an operator scanning 3 000
    /// rows can find it in their file.
    ///
    /// <para>⚠️ The guillemets carry a <b>narrow no-break space</b> (<c>U+202F</c>), which is what binds the
    /// closing mark to the value. The browser has <c>quoteFr()</c> for this and the reason is the same on both
    /// sides: an ordinary space is a break opportunity, so a long value leaves the closing guillemet alone on a
    /// line of its own — and unlike static prose, the width of a phone number typed by a stranger is unknown
    /// when the sentence is written.</para>
    /// </summary>
    public static string InvalidRow(string rawPhone) =>
        $"Numéro de téléphone invalide : « {rawPhone} ». Choisissez le pays, "
        + "ou saisissez le numéro au format international (+33…).";

    /// <summary>
    /// An emergency contact's number that could not be parsed. ⚠️ A <b>warning</b>, never a refusal: nothing
    /// dispatches to this number — a human reads it in an emergency — and losing a patient's whole record
    /// because a relative's number is written « 71 555 (bureau) » protects a field nobody sends to. The value is
    /// imported as typed (AC-14).
    /// </summary>
    public static string EmergencyRowNotRecognised(string rawPhone) =>
        $"Téléphone d'urgence non reconnu : « {rawPhone} ». Importé tel quel.";
}
