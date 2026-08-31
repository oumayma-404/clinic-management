namespace ClinicManagement.Application.Common;

/// <summary>
/// Whether two patient names are the <b>same name written differently</b> — « Chaïma Ben Khalifa » and
/// « Chaima Benkhalifa », « Zouari Fatma » and « Fatma Zouari », « Mohammed » and « Mohamed ».
///
/// <para><b>This is an equivalence of spellings, not a distance.</b> There is no edit budget anywhere in it: two
/// names either reduce to the same canonical form or they do not. That is the whole design — an edit budget was
/// measured first and rejected, because a per-half budget of 1–3 edits claimed 16 different names out of a
/// 46-name corpus were the same person (« Imen »/« Iman », « Olfa »/« Alfa », « Hamza »/« Hamdi »), while this
/// rule claims none. A budget also cannot be reasoned about: loosening it by one is untestable in the small and
/// catastrophic in the large. See <c>features/calendar-import-duplicate-merge/spec.md</c> AC-2 to AC-6.</para>
///
/// <para>⚠️ <b>It never links anything by itself.</b> The Google Calendar import books an event onto an existing
/// patient only on a byte-exact name match (<c>GoogleCalendarSyncService.MatchesName</c>); an equivalence found
/// here produces a <i>question for a human</i> and nothing else. The substring match that once stood in the exact
/// path made « Ali » match « Ali Ben Salah » and booked events onto the wrong file — this class is a looser test
/// still, so it must never reach that position.</para>
///
/// <para>⚠️ A name is compared as <b>two strings, a given name and a surname</b>, never as one blob. A blob lets
/// a difference in the surname be paid for by the given name; and both halves being required is what keeps
/// siblings apart (« Ali » and « Sami Ben Salah » share a surname exactly and are still not a match).</para>
/// </summary>
public static class PatientNameEquivalence
{
    /// <summary>
    /// The transliteration table, <b>in application order</b>. Order is load-bearing: <c>ch</c> is mapped to a
    /// sentinel first because <c>c</c>→<c>s</c> would otherwise canonicalise « Chaima » into « Samia », which is a
    /// different patient. <c>PatientNameEquivalenceTests</c> pins that pair.
    /// </summary>
    private static readonly (string From, string To)[] Table =
    {
        ("ch", "1"),   // sentinel, not 'c' — protected before c→s runs
        ("x", "ks"),   // Sfaxi / Sfaksi
        ("kh", "k"), ("gh", "g"), ("ph", "f"),
        ("ou", "u"), ("w", "u"),
        ("y", "i"),
        ("c", "s"), ("k", "q"),
    };

    /// <summary>
    /// True when the two (given name, surname) pairs are the same name written differently, <b>in either order</b>
    /// — a surname typed first is an ordinary way to enter a name, not a fallback. Any blank half is never a match.
    /// </summary>
    public static bool AreWritingVariants(string? firstA, string? lastA, string? firstB, string? lastB)
    {
        var fa = Canonicalize(firstA);
        var la = Canonicalize(lastA);
        var fb = Canonicalize(firstB);
        var lb = Canonicalize(lastB);

        if (fa.Length == 0 || la.Length == 0 || fb.Length == 0 || lb.Length == 0)
        {
            return false;
        }

        return (fa == fb && la == lb) || (fa == lb && la == fb);
    }

    /// <summary>
    /// Splits a calendar title into a given name and a surname: the first token, then everything after it joined.
    /// « Mohamed Ben Salah » is <c>Mohamed</c> + <c>Ben Salah</c> — a multi-token surname is the ordinary case
    /// here, so the split is into two <i>parts</i> and never a requirement of two words. Returns null for a title
    /// that cannot yield both halves.
    /// </summary>
    public static (string First, string Last)? SplitTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var parts = title.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? null : (parts[0].Trim(), string.Join(" ", parts.Skip(1)).Trim());
    }

    /// <summary>
    /// One half of a name reduced to the form every accepted spelling of it shares. Steps, in order: fold accents
    /// and case, drop everything that is not a letter (so <c>Benkhalifa</c> = <c>Ben Khalifa</c>), apply
    /// <see cref="Table"/>, collapse repeated letters, drop <c>h</c>, drop a trailing <c>e</c>.
    /// </summary>
    public static string Canonicalize(string? part)
    {
        var folded = SearchTerm.Normalize(part);
        if (folded.Length == 0)
        {
            return string.Empty;
        }

        var letters = new string(folded.Where(char.IsLetter).ToArray());
        foreach (var (from, to) in Table)
        {
            letters = letters.Replace(from, to);
        }

        letters = CollapseRuns(letters);
        letters = letters.Replace("h", string.Empty);   // whatever h survives kh/gh/ph is silent
        return letters.EndsWith('e') ? letters[..^1] : letters;
    }

    /// <summary>
    /// Any run of one repeated letter becomes one letter — « Mohammed »/« Mohamed », « Aniss »/« Anis »,
    /// « Salmaa »/« Salma », « Chaabane »/« Chabane ». Wherever the run sits, including the end of the part.
    /// </summary>
    private static string CollapseRuns(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (builder.Length == 0 || builder[^1] != ch)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
