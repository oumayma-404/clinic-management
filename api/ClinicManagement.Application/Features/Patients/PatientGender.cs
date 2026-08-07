using ClinicManagement.Application.Common;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// The patient's <c>Gender</c> as it is <b>stored</b> and as it <b>reads in a file</b> — both directions, one place.
///
/// <para><b>Why the server needs this at all.</b> The stored values are English tokens (<c>Male</c>/<c>Female</c>/
/// <c>Other</c>/<c>Unknown</c>) and the French labels have always lived in the browser
/// (<c>components/appointment-labels.ts</c>), which is correct while the only reader is a screen. A <b>CSV is a
/// reader too</b>, and the L5 export wrote the raw token — so « Sexe : Male » appeared in a file whose every other
/// column is French, next to a « Archivé : Oui » that had been translated on purpose. That is the same defect
/// <c>CsvCell.YesNo</c>'s doc block names (« <c>True</c>/<c>False</c> is not a translation »), one column over.</para>
///
/// <para>⚠️ <b>Both directions live here so they cannot drift.</b> The import has to parse what the export writes —
/// « export → import → identical set » is the round trip the spec asks for — so a formatter without its matching
/// parser would make this product's own file the one file it cannot re-read. <see cref="Parse"/> therefore accepts
/// the French labels <i>and</i> the stored tokens <i>and</i> the single letters a spreadsheet really contains.</para>
/// </summary>
public static class PatientGender
{
    public const string Male = "Male";
    public const string Female = "Female";
    public const string Other = "Other";

    /// <summary>
    /// Written by three separate paths (the create fallback, the appointment dialog's inline create, the
    /// Google→App placeholder patient), so it exists in real data and must have a French rendering.
    /// </summary>
    public const string Unknown = "Unknown";

    /// <summary>The French label, for a file or any other server-rendered surface. Blank stays blank.</summary>
    public static string Label(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return string.Empty;
        }

        return SearchTerm.Normalize(stored) switch
        {
            "male" => "Homme",
            "female" => "Femme",
            "other" => "Autre",
            "unknown" => "Non précisé",
            // A clinic's own value passes through verbatim rather than becoming « Inconnu »: it is the practice's
            // data, and blanking it in the file they exported to keep would be a loss, not a translation.
            _ => stored.Trim(),
        };
    }

    /// <summary>
    /// A cell's value → the stored token, or <c>null</c> when it means nothing recognisable.
    ///
    /// <para><c>null</c> rather than <see cref="Unknown"/> so the caller can tell « the column said nothing » from
    /// « the column said something I could not read » — the first is silent, the second earns a warning on the row.</para>
    /// </summary>
    public static string? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return SearchTerm.Normalize(raw) switch
        {
            "homme" or "male" or "m" or "h" or "masculin" => Male,
            "femme" or "female" or "f" or "feminin" => Female,
            "autre" or "other" or "o" => Other,
            "non precise" or "unknown" or "inconnu" => Unknown,
            _ => null,
        };
    }
}
