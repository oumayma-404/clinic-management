using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Domain.ValueObjects;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// The CNAM closed-set contract between the server and the browser: <c>web/lib/cnam.ts</c> must declare exactly the
/// régime and lien values <see cref="CnamInfo"/> does, character for character.
/// </summary>
/// <remarks>
/// <para>
/// These are <b>stored values, not display labels</b> — the opposite of the « English key + French label » mapping
/// used elsewhere. The BS1 renderer ticks the régime and lien boxes by matching these strings, so a mismatch in
/// casing or in a single accent (« Convention bilatérale » carries one) makes the renderer's <c>switch</c> fall
/// through: the box prints <b>empty</b>, no layer raises anything, and the bulletin is refused at the caisse
/// looking complete on screen. There is no exception to catch and no type to fail — which is exactly why this has
/// to be a test.
/// </para>
/// <para>
/// The browser needs its own copy because `adoption-qa-k` K2 marks the missing mandatory fields in the editor
/// *before* Save, naming each one. It got a real module (`lib/cnam.ts`) rather than a second set of literals, and
/// this class is what keeps the two from drifting. Derived from both sides — it parses the TypeScript rather than
/// restating its contents — for the same reason <c>RealtimeResourceResolverTests</c> does: a hand-maintained
/// expectation list can only fail on the rows someone remembered to write.
/// </para>
/// </remarks>
public class CnamClosedSetContractTests
{
    // ---- Parsing the frontend module ------------------------------------------------

    /// <summary>Reads a `export const NAME = ["a", "b"] as const` array out of <c>web/lib/cnam.ts</c>.</summary>
    private static IReadOnlyList<string> DeclaredArray(string constName)
    {
        var source = File.ReadAllText(CnamModulePath());

        var block = Regex.Match(
            source,
            $@"export\s+const\s+{Regex.Escape(constName)}\s*(?::[^=]+)?=\s*\[(?<body>[^\]]*)\]",
            RegexOptions.Singleline);

        Assert.True(block.Success, $"Could not find `export const {constName} = [...]` in web/lib/cnam.ts.");

        return Regex.Matches(block.Groups["body"].Value, @"""(?<value>[^""]*)""")
            .Select(m => m.Groups["value"].Value)
            .ToList();
    }

    /// <summary>Reads a `export const NAME = 10` numeric literal out of the same module.</summary>
    private static int DeclaredNumber(string constName)
    {
        var source = File.ReadAllText(CnamModulePath());

        var match = Regex.Match(source, $@"export\s+const\s+{Regex.Escape(constName)}\s*=\s*(?<value>\d+)");
        Assert.True(match.Success, $"Could not find `export const {constName} = <number>` in web/lib/cnam.ts.");

        return int.Parse(match.Groups["value"].Value);
    }

    /// <summary>
    /// Locates <c>web/lib/cnam.ts</c> from this source file's own compile-time path — deliberately NOT from
    /// <c>AppContext.BaseDirectory</c>, since the suite is routinely built to an output directory outside the
    /// repository (the Smart App Control workaround). Same approach as <c>RealtimeResourceResolverTests</c>.
    /// </summary>
    private static string CnamModulePath([CallerFilePath] string thisFile = "")
    {
        const string relative = "web/lib/cnam.ts";
        var native = relative.Replace('/', Path.DirectorySeparatorChar);

        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, native);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Fail loudly rather than skipping: a contract test that goes quiet when it cannot find one side reports
        // green while the thing it guards is unchecked.
        throw new FileNotFoundException(
            $"Could not locate '{relative}' walking up from '{thisFile}'. The CNAM closed-set contract cannot be "
            + "verified without the frontend module.");
    }

    // ---- The contract ---------------------------------------------------------------

    [Fact] // [K2] The régime set is identical on both sides, in order and in spelling.
    public void Regimes_Match_The_Domain_Exactly()
    {
        // Ordered comparison, not set equality: the order is what the patient dialog renders, and « CNSS » first
        // is a deliberate choice rather than an accident of the literal.
        Assert.Equal(CnamInfo.AllowedRegimes, DeclaredArray("CNAM_REGIMES"));
    }

    [Fact] // [K2] The lien set is identical on both sides.
    public void Liens_Match_The_Domain_Exactly()
    {
        Assert.Equal(CnamInfo.AllowedLiens, DeclaredArray("CNAM_LIENS"));
    }

    [Fact] // [K2] And the subset that also needs a rang.
    public void Liens_Requiring_A_Rang_Match_The_Domain_Exactly()
    {
        Assert.Equal(CnamInfo.LiensRequiringRang, DeclaredArray("CNAM_LIENS_REQUIRING_RANG"));
    }

    [Fact] // [K7] The comb's cell count is one number, not two.
    public void Identifiant_Digit_Count_Matches_The_Domain()
    {
        // If these ever disagree the editor and the server refuse different identifiants: the browser would let a
        // number through that the write then rejects, or block one the form could actually print.
        Assert.Equal(CnamInfo.IdentifiantUniqueDigits, DeclaredNumber("CNAM_IDENTIFIANT_DIGITS"));
    }

    [Fact] // [K2] The accented value is present verbatim — the specific spelling that used to fail silently.
    public void The_Accented_Regime_Is_Spelled_With_Its_Accent_On_Both_Sides()
    {
        // Asserted explicitly as well as through the set comparison above. The set comparison would catch this,
        // but this case names the defect, so a future reader knows which character the test is really about.
        Assert.Equal("Convention bilatérale", CnamInfo.RegimeConventionBilaterale);
        Assert.Contains("Convention bilatérale", DeclaredArray("CNAM_REGIMES"));
    }

    [Fact] // [K2] Nothing outside the declared sets is smuggled in as a literal elsewhere in the module.
    public void The_Module_Declares_No_Other_Regime_Or_Lien_Literals()
    {
        // Guards the shape rather than the values: if someone adds a fifth lien as a bare string in a helper
        // instead of extending the array, the set comparisons above would still pass while the browser accepted a
        // value the server refuses.
        //
        // ⚠️ Comments are stripped first. Without that this scans the module's own prose — which quotes words like
        // "normalise" and discusses the values it documents — and reports a match spanning two unrelated quotes.
        // A guard that fires on its own documentation gets deleted rather than fixed, so it strips first.
        var source = StripComments(File.ReadAllText(CnamModulePath()));
        var declared = DeclaredArray("CNAM_REGIMES")
            .Concat(DeclaredArray("CNAM_LIENS"))
            .Concat(DeclaredArray("CNAM_LIENS_REQUIRING_RANG"))
            .ToHashSet(StringComparer.Ordinal);

        // Every capitalised double-quoted literal left in the code must be one of the declared values.
        var suspicious = Regex.Matches(source, @"""(?<value>[^""\r\n]*[A-ZÉÈÀÊ][^""\r\n]*)""")
            .Select(m => m.Groups["value"].Value)
            .Where(value => !declared.Contains(value))
            .ToList();

        Assert.Empty(suspicious);
    }

    /// <summary>Removes `/* … */` and `// …` comments so a scan sees code rather than documentation.</summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlocks, @"//[^\r\n]*", string.Empty);
    }
}
