using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The frontend keeps its own copy of the tooth-condition set — it needs a French label and a hex per member,
/// neither of which belongs in the Domain — so this parses that file and fails the build when the two drift.
///
/// <para>The same shape as <c>RealtimeResourceResolverTests</c>, and for the same reason: a mirror nobody checks
/// is a mirror that is already wrong. Both directions are asserted, so a member added on either side alone
/// fails — a condition charted by the server with no label renders as « Sain » on the chart, and one offered by
/// the picker with no enum member is refused on save with a validation error naming a value the user just
/// chose from a list.</para>
/// </summary>
public class OdontogramConditionMirrorTests
{
    /// <summary>
    /// Located from this source file's own compile-time path, deliberately NOT from
    /// <c>AppContext.BaseDirectory</c>: the suite is routinely built to an output directory outside the
    /// repository (the Smart App Control workaround), which makes a walk-up from the binary fail.
    /// </summary>
    private static string ConditionsFile([CallerFilePath] string thisFile = "")
    {
        const string relative = "web/components/odontogram-conditions.ts";
        var native = relative.Replace('/', Path.DirectorySeparatorChar);

        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, native);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        // Fail loudly: a contract test that skips when it cannot find one side reports green while the contract
        // it guards goes unchecked.
        throw new FileNotFoundException(
            $"Could not locate '{relative}' walking up from '{thisFile}'. The odontogram condition set cannot be "
            + "verified without the frontend style map.");
    }

    /// <summary>The keys of the exported `CONDITIONS` record.</summary>
    private static HashSet<string> FrontendConditions()
    {
        var body = Section(ConditionsFile(), "export const CONDITIONS: Record<string, ConditionStyle> = {", "}");
        return Regex.Matches(body, @"^\s*(\w+):\s*\{", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
    }

    private static HashSet<string> FrontendNeedsTreatment()
    {
        var file = ConditionsFile();
        var start = file.IndexOf("export const NEEDS_TREATMENT_CONDITIONS", StringComparison.Ordinal);
        Assert.True(start >= 0, "NEEDS_TREATMENT_CONDITIONS not found");
        var end = file.IndexOf("] as const", start, StringComparison.Ordinal);
        Assert.True(end > start, "NEEDS_TREATMENT_CONDITIONS is not the expected array literal");
        return Regex.Matches(file[start..end], "\"(\\w+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
    }

    /// <summary>
    /// The body of an export, cut at its own closing token at column 0. The token is a parameter because the two
    /// exports read here close differently — a record with <c>}</c>, an array with <c>]</c> — and cutting the
    /// array at the next <c>}</c> ran on into <c>CONDITION_FAMILY</c>, whose values then read as condition names.
    /// </summary>
    private static string Section(string file, string header, string closer)
    {
        var start = file.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"not found: {header}");
        var end = file.IndexOf("\n" + closer, start, StringComparison.Ordinal);
        Assert.True(end > start, $"unterminated: {header}");
        return file[start..end];
    }

    [Fact]
    public void Every_ToothCondition_Has_A_Frontend_Style()
    {
        var frontend = FrontendConditions();
        foreach (var condition in Enum.GetNames<ToothCondition>())
        {
            Assert.True(
                frontend.Contains(condition),
                $"`{condition}` has no entry in web/components/odontogram-conditions.ts — it would render as « Sain »");
        }
    }

    [Fact]
    public void The_Frontend_Invents_No_Condition_The_Server_Refuses()
    {
        var server = Enum.GetNames<ToothCondition>().ToHashSet();
        foreach (var condition in FrontendConditions())
        {
            Assert.True(server.Contains(condition), $"`{condition}` is not a ToothCondition — saving it would fail");
        }
    }

    [Fact]
    public void The_Order_List_Covers_Every_Condition_Exactly_Once()
    {
        var body = Section(ConditionsFile(), "export const CONDITION_ORDER = [", "]");
        var ordered = Regex.Matches(body, "\"(\\w+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(ordered.Count, ordered.Distinct().Count());
        Assert.Equal(Enum.GetNames<ToothCondition>().ToHashSet(), ordered.ToHashSet());
    }

    // The set that decides what seeds a plan and what is counted as « à traiter ». Drift here is silent on both
    // sides: the server would seed a line the chart does not flag, or flag one the server will not seed.
    [Fact]
    public void Needs_Treatment_Agrees_With_The_Server()
    {
        var expected = ConditionTreatments.NeedsTreatment.Select(c => c.ToString()).ToHashSet();
        Assert.Equal(expected, FrontendNeedsTreatment());
    }
}
