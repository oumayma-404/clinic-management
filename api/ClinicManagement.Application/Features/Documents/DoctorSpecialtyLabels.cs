namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// The French label for a stored (English) <c>Doctor.Specialty</c> key.
///
/// <para>
/// ⚠️ <b>The server needed this the moment it began issuing documents itself.</b> Until the fiche de soins
/// emitted its own ordonnance, every document was created by the browser, which sent an already-translated
/// <c>doctorSpecialty</c> through <c>web/lib/specialties.ts</c>. A document composed server-side has only
/// <c>Doctor.Specialty</c>, whose keys are deliberately English (they are the values already stored on every
/// row and snapshotted onto every <c>MedicalDocument</c> ever issued — see that TS file for why renaming them
/// is not an option). Without this map an ordonnance would print « Orthodontist » under the prescriber's name,
/// on a French legal document.
/// </para>
///
/// <para>
/// ⚠️ <b>This is the C# half of a pair.</b> <c>web/lib/specialties.ts</c> is the other, and the two must hold
/// the same keys and the same labels: a specialty added to one and not the other prints English on paper while
/// reading correctly on screen. <c>check:responsive</c>'s <c>specialty-labels-have-one-owner</c> compares them
/// in both directions.
/// </para>
///
/// <para>
/// The fallback is <b>verbatim, never blank</b> — for the same two reasons the TS twin gives: a clinic that
/// typed a custom specialty keeps it, and documents issued before either map existed already hold French
/// snapshots (« Médecin dentiste », « Chirurgien maxillo-facial ») that must pass straight through.
/// </para>
/// </summary>
public static class DoctorSpecialtyLabels
{
    private static readonly Dictionary<string, string> LabelsFr = new(StringComparer.Ordinal)
    {
        ["Dentist"] = "Médecin dentiste",
        ["Orthodontist"] = "Orthodontiste",
        ["Prosthodontist"] = "Prothodontiste",
        ["Endodontist"] = "Endodontiste",
        ["Periodontist"] = "Parodontiste",
        ["Oral Surgeon"] = "Chirurgien buccal",
        ["Pediatric Dentist"] = "Pédodontiste",
    };

    /// <summary>The keys this map knows — exposed so the guard test can compare the two halves.</summary>
    public static IReadOnlyDictionary<string, string> All => LabelsFr;

    /// <summary>
    /// The French label for a stored specialty, or the stored value trimmed and verbatim when it has none.
    /// Empty in, empty out.
    /// </summary>
    public static string Label(string? specialty)
    {
        if (string.IsNullOrWhiteSpace(specialty))
        {
            return string.Empty;
        }

        var trimmed = specialty.Trim();
        return LabelsFr.TryGetValue(trimmed, out var label) ? label : trimmed;
    }
}
