using System.Globalization;
using System.Text;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// The clinical discipline a <see cref="Entities.ProcedureType"/> belongs to — « Endodontie », « Prothèse fixe » —
/// and the single authority on how a typed one is spelled.
/// <para>
/// The category is deliberately <b>open</b>: a clinic may invent « Occlusodontie » and it is kept verbatim, because
/// the act catalogue itself is clinic-authored and a closed enum would mean a code change to file an act the
/// practice already performs. <see cref="Canonical"/> is therefore a <i>suggestion</i> set, not a constraint.
/// </para>
/// <para>
/// ⚠️ <b>What makes an open set survivable is <see cref="Normalize"/>, and every write path must go through it.</b>
/// The whole value of a category is that acts sharing one are grouped together; « endodontie », « ENDODONTIE » and
/// « Endodontie&#160; » are three groups to a database and one to a dentist, so an admin who types instead of
/// picking from the suggestions would silently shard a discipline. Folding a typed value back onto the canonical
/// spelling costs nothing and removes that entire failure mode — while a genuinely new label, which folds onto
/// nothing, passes through untouched.
/// </para>
/// </summary>
public static class ProcedureTypeCategories
{
    /// <summary>
    /// The twelve disciplines a Tunisian private dental practice divides its acts into, in the order a course of
    /// treatment runs (consultation → radiologie → soins → … → pédodontie), <b>not</b> alphabetically: the
    /// catalogue is read to find the act you are about to perform, and « Consultation » before « Chirurgie » is
    /// how a session actually goes.
    /// <para>
    /// Mirrors the categories <c>ProcedureTypeCatalogSeed</c> already assigned its rows — this list did not invent
    /// a taxonomy, it promoted the one the seed had been smuggling through the <c>Description</c> column. Five of
    /// them are also the vocabulary the CNAM nomenclature catalogue uses, so an act and its CNAM entry can be
    /// filed under the same word.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> Canonical = new[]
    {
        "Consultation",
        "Radiologie",
        "Soins conservateurs",
        "Endodontie",
        "Parodontologie",
        "Chirurgie/Extraction",
        "Prothèse fixe",
        "Prothèse amovible",
        "Implantologie",
        "Orthodontie",
        "Esthétique",
        "Pédodontie",
    };

    /// <summary>
    /// The canonical spelling of <paramref name="value"/> if it names one of the <see cref="Canonical"/>
    /// disciplines, the trimmed value if it is a category of the clinic's own, or <c>null</c> for blank.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var folded = Fold(trimmed);

        foreach (var canonical in Canonical)
        {
            if (Fold(canonical) == folded)
            {
                return canonical;
            }
        }

        return trimmed;
    }

    /// <summary>Whether <paramref name="value"/> names one of the suggested disciplines.</summary>
    public static bool IsCanonical(string? value) =>
        value is not null && Canonical.Any(c => Fold(c) == Fold(value));

    /// <summary>
    /// Accent-, case-, space- and punctuation-insensitive comparison key.
    /// <para>
    /// Punctuation is dropped rather than kept because of « Chirurgie/Extraction » specifically: it is written
    /// « Chirurgie / Extraction » and « Chirurgie-Extraction » about as often as with the bare slash, and all
    /// three name one discipline.
    /// </para>
    /// </summary>
    private static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}
