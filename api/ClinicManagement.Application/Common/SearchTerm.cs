using System.Globalization;
using System.Text;

namespace ClinicManagement.Application.Common;

/// <summary>
/// The single authority on what a free-text search box means, shared by every paginated list.
///
/// <para><b>Why this exists as a type.</b> Search became load-bearing the moment lists were paginated: before,
/// a table held every row and filtered in the browser, so « search » and « what is on screen » were the same
/// set by construction. With a page of 25, a search that only looks at the loaded page silently answers a
/// different question from the one the user asked — they type a patient's name, the name is on page 7, and the
/// screen says « aucun résultat ». So search has to run in the database, over every row, and the normalisation
/// rule has to be identical on both sides of that boundary: the C# that prepares the term and the SQL that
/// matches the column. Two implementations of "case- and accent-insensitive" is the § 5.10 defect again — the
/// first accented name that matches in one and not the other is indistinguishable from a missing patient.</para>
///
/// <para><b>Accents.</b> Tunisian and French names carry them routinely (Amïne, Béchir, Moncef Chaâbane) and
/// nobody types them into a search box. The database side is PostgreSQL's <c>unaccent</c>; this side is
/// Unicode decomposition. They agree on the Latin-1 range that matters here — <c>é è ê ë à â ï î ô û ù ç</c>
/// all fold to their ASCII base in both. <see cref="Normalize"/> is what the old private copy in
/// <c>GetPatientsQuery</c> did, moved here rather than copied.</para>
/// </summary>
public static class SearchTerm
{
    /// <summary>
    /// The character that escapes a LIKE metacharacter in the patterns this class builds. Callers passing a
    /// pattern to <c>ILike</c> must pass this as the escape character or the escaping is inert.
    /// </summary>
    public const char LikeEscape = '\\';

    /// <summary>
    /// Lowercase + strip diacritics, or empty for a blank term. Returns empty (never null) so callers can test
    /// with <c>string.IsNullOrEmpty</c> and pass the result straight into a comparison.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>
    /// Turn a raw search box value into a <c>%…%</c> LIKE pattern, or null when there is nothing to search for.
    ///
    /// <para><b>Wildcards in the term are escaped, deliberately.</b> A dentist typing <c>%</c> or <c>_</c> —
    /// or pasting a phone number written <c>21_555</c> — means those characters literally. Unescaped, <c>%</c>
    /// matches every row (so the filter silently does nothing) and <c>_</c> matches any single character (so
    /// the result set is wrong in a way nobody would notice). The escape character itself has to be escaped
    /// first, or escaping <c>%</c> would produce a dangling backslash.</para>
    /// </summary>
    public static string? ToLikePattern(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        var escaped = normalized
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        return $"%{escaped}%";
    }

    /// <summary>
    /// In-memory equivalent of the SQL predicate, for the handful of reads whose rows are already materialised
    /// for another reason (a merged multi-ledger list, a projection with no queryable source). Uses the same
    /// <see cref="Normalize"/>, so a term matches the same rows either way.
    /// </summary>
    public static bool Matches(string? term, params string?[] fields)
    {
        var normalized = Normalize(term);
        if (string.IsNullOrEmpty(normalized))
        {
            return true;
        }

        foreach (var field in fields)
        {
            if (Normalize(field).Contains(normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
