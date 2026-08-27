using System.Globalization;
using System.Text;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// The comparison key an <b>open category set</b> folds on — accent-, case-, space- and punctuation-insensitive.
/// <para>
/// Three category sets in this product are open-with-suggestions (<see cref="ProcedureTypeCategories"/>,
/// <see cref="SupplierCategories"/>, <see cref="StockCategories"/>), and every one of them survives being open
/// only because a typed variant folds back onto the canonical spelling. Three private copies of that fold is the
/// shape this repo keeps finding: they agree today and the first one to gain a rule the others do not is the one
/// that silently shards a category.
/// </para>
/// <para>
/// Punctuation is dropped rather than kept because of « Chirurgie/Extraction » specifically: it is written
/// « Chirurgie / Extraction » and « Chirurgie-Extraction » about as often as with the bare slash, and all three
/// name one discipline.
/// </para>
/// </summary>
public static class CategoryFolding
{
    public static string Fold(string value)
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

    /// <summary>
    /// The canonical spelling of <paramref name="value"/> if it folds onto one of <paramref name="canonical"/>,
    /// the trimmed value if it is a label of the clinic's own, or <c>null</c> for blank. The shared body of every
    /// open set's <c>Normalize</c>.
    /// </summary>
    public static string? NormalizeAgainst(string? value, IReadOnlyList<string> canonical)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var folded = Fold(trimmed);

        foreach (var candidate in canonical)
        {
            if (Fold(candidate) == folded)
            {
                return candidate;
            }
        }

        return trimmed;
    }

    /// <summary>Whether <paramref name="value"/> folds onto one of <paramref name="canonical"/>.</summary>
    public static bool IsCanonicalIn(string? value, IReadOnlyList<string> canonical) =>
        value is not null && canonical.Any(c => Fold(c) == Fold(value));
}
