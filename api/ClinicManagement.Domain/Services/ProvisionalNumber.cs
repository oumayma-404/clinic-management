using System.Text.RegularExpressions;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// A note, devis or avoir numbered on a PC de secours during a cut (<c>clinic-pc-copy</c>, owner 2026-10-13): only the
/// cloud gives legal numbers, so the PC gives <c>AAAA-P</c> plus the first eight hex digits of the document's id, and
/// the return replaces it with the cloud's next real number. Unique by construction (the id), never confused with a
/// real number (whose sequence is digits only, so every <c>GetMaxSequenceForYearAsync</c> already skips it), and the
/// year it was issued in is kept for the renumbering.
/// </summary>
public static partial class ProvisionalNumber
{
    /// <summary>The SQL <c>~</c> pattern of a provisional number — the return renumbers exactly these.</summary>
    public const string SqlPattern = "^[0-9]{4}-P[0-9A-F]{8}$";

    /// <summary>The provisional form of <paramref name="number"/> (<c>AAAA-NNNN</c>) for the document <paramref name="documentId"/>.</summary>
    public static string For(string number, Guid documentId)
    {
        // Every numbering path writes « AAAA-NNNN » from the clinic's own year (ClinicClock): that year is kept.
        if (number.Length < 5 || !number[..4].All(char.IsAsciiDigit) || number[4] != '-')
        {
            throw new ArgumentException($"Numéro sans année : « {number} ».", nameof(number));
        }

        return $"{number[..4]}-P{documentId.ToString("N")[..8].ToUpperInvariant()}";
    }

    public static bool IsProvisional(string? number) => number is not null && Pattern().IsMatch(number);

    [GeneratedRegex(SqlPattern)]
    private static partial Regex Pattern();
}
