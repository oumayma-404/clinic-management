using ClinicManagement.Infrastructure.Deployment;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The guard that keeps <c>IsLocalMode</c> retired (multi-tenant-cloud, US-1 / Part A).
///
/// <para><b>Why a source scan and not a list of files to check.</b> The defect this refactor removes is one
/// boolean answering a dozen unrelated questions; the way it comes back is not somebody editing a site this test
/// already knows about, but a <i>new</i> branch asking the old question again. So the set of occurrences is
/// derived by reading every C# file in the solution, and the assertion is that it equals the three files below —
/// exactly the discipline <c>RealtimeResourceResolverTests</c> and <c>verify-schema</c> use, and the opposite of
/// the hand-maintained allow-lists this repo has repeatedly watched rot.</para>
///
/// <para><b>Reading its own needle.</b> The search text is assembled at run time, so this file does not itself
/// contain the pattern and needs no exemption from its own scan — a guard that has to exclude itself is one
/// rename away from excluding what it was watching.</para>
/// </summary>
public class DeploymentProfileCoverageTests
{
    /// <summary>
    /// The <b>only</b> files allowed to name the old boolean, each with the reason it is here. Three entries,
    /// and every one of them is asserted to exist — an exemption naming a file that was renamed away would
    /// otherwise be a pre-approved hole.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LocalAuthConfig.cs"] = "declares it — Auth:Mode is still what a profile is derived from when the "
                                 + "Deployment:Profile key is absent",
        ["DeploymentProfile.cs"] = "the single back-compat call, inside Resolve",
        ["DeploymentProfileTests.cs"] = "proves the two shipped profiles reproduce the old truth table (R-2) by "
                                        + "asserting against the boolean itself rather than a retyped copy of it"
    };

    private static string Needle() => "IsLocalMode" + "(";

    // The solution walk and the bin/obj rule live in SolutionSources, shared with the other derived guards.
    private static DirectoryInfo SolutionDirectory() => SolutionSources.Root();

    private static IEnumerable<string> SourceFiles(DirectoryInfo root) => SolutionSources.CsFiles(root);

    [Fact]
    public void The_old_mode_boolean_is_named_only_where_it_is_still_legitimate()
    {
        var root = SolutionDirectory();
        var needle = Needle();

        var offenders = SourceFiles(root)
            .Where(path => File.ReadAllText(path).Contains(needle, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root.FullName, path))
            .Where(relative => !AllowedFiles.ContainsKey(Path.GetFileName(relative)))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These files still ask the old mode boolean:{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders.Select(o => "  " + o))
            + $"{Environment.NewLine}Each branch must ask the named capability it actually means "
            + $"({nameof(DeploymentProfile)}). If a genuinely new site needs the boolean itself, add it to "
            + "AllowedFiles with the reason — do not widen the scan.");
    }

    [Fact]
    public void Every_exemption_still_names_a_file_that_exists()
    {
        var root = SolutionDirectory();
        var present = SourceFiles(root).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (file, reason) in AllowedFiles)
        {
            Assert.True(present.Contains(file), $"Exempted file '{file}' ({reason}) no longer exists.");
        }
    }

    /// <summary>
    /// The one allowed production call is inside <c>Resolve</c>, which is the back-compat derivation. Anywhere
    /// else in that file it would be a capability quietly re-deriving itself from configuration — the thing
    /// <c>LEARNINGS.md</c> warns about.
    /// </summary>
    [Fact]
    public void The_production_call_sits_inside_Resolve()
    {
        var root = SolutionDirectory();
        var path = SourceFiles(root).Single(p => Path.GetFileName(p) == "DeploymentProfile.cs");
        var lines = File.ReadAllLines(path);
        var needle = Needle();

        var hits = lines
            .Select((text, index) => (Index: index, Text: text))
            .Where(l => l.Text.Contains(needle, StringComparison.Ordinal))
            .ToList();
        // Anchored on the DECLARATIONS: `Resolve(` and `For(` on their own also match the call sites, and
        // Resolve's body calls For on the very line being located — which would make the range check pass by
        // coincidence rather than by containment.
        var declaration = $"{nameof(DeploymentProfile)} ";
        var resolveLine = Array.FindIndex(lines, l => l.Contains($"{declaration}{nameof(DeploymentProfile.Resolve)}("));
        var forLine = Array.FindIndex(lines, l => l.Contains($"{declaration}{nameof(DeploymentProfile.For)}("));

        Assert.True(resolveLine >= 0 && forLine > resolveLine, "Could not locate Resolve/For in DeploymentProfile.cs.");
        var hit = Assert.Single(hits);
        Assert.InRange(hit.Index, resolveLine, forLine - 1);
    }
}
