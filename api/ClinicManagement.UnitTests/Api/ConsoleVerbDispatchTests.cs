using System.Reflection;
using ClinicManagement.UnitTests.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// <b>Every console verb this build contains is actually dispatched by <c>Program.cs</c>.</b>
///
/// <para><b>The failure it catches.</b> A verb with no branch does not error — it falls through and boots the
/// <b>web host</b> with the verb's own arguments. To an operator that reads as « the command did nothing », and
/// in a container it reads as a second API starting up. `SubscriptionVendorCommandReachabilityTests` already
/// holds this for the five subscription verbs; this holds it for <b>all</b> of them, derived from the assembly,
/// so the next verb written is covered on the day it is written rather than the day somebody remembers to add
/// a row here — which is the same derived-vs-listed lesson <c>verify-schema</c> and
/// <c>RealtimeResourceResolverTests</c> embody.</para>
///
/// <para>⚠️ The candidate filter is <c>!IsAbstract || IsSealed</c>, not the ordinary <c>IsAbstract: false</c>:
/// every verb here is a <c>static class</c>, which is abstract <b>and</b> sealed in metadata, so the obvious
/// filter matches none of them and the guard passes over an empty set. That exact mistake left
/// <c>SystemWideCallerCoverageTests</c>' console branch matching nothing for four features — hence the
/// non-vacuity assertion below.</para>
/// </summary>
public class ConsoleVerbDispatchTests
{
    /// <summary>Every type in the maintenance namespace declaring a verb name.</summary>
    private static IReadOnlyList<Type> Verbs() =>
        typeof(ClinicManagement.API.Startup.InstallConfiguration).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsNested: false } && (!t.IsAbstract || t.IsSealed))
            .Where(t => t.Namespace == "ClinicManagement.API.Maintenance")
            .Where(t => NameField(t) is not null)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static FieldInfo? NameField(Type type) =>
        type.GetField("CommandName", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

    private static string NameOf(Type verb) => (string)NameField(verb)!.GetValue(null)!;

    /// <summary>
    /// Both halves: the name is matched, and the verb is actually invoked. ⚠️ The entry point is
    /// <c>.Run</c> rather than a fixed signature on purpose — the verbs legitimately differ
    /// (<c>Run(args)</c> · <c>RunAsync()</c> · <c>RunAsync(args)</c>), and a check pinned to one of those
    /// reported three perfectly-dispatched verbs as missing when it was first written. Loosening a check is
    /// where it silently stops matching anything, which is why the red proof below re-proves it.
    /// </summary>
    private static bool IsDispatched(string program, Type verb) =>
        program.Contains($"{verb.Name}.CommandName", StringComparison.Ordinal)
        && program.Contains($"{verb.Name}.Run", StringComparison.Ordinal);

    private static string ProgramSource() =>
        File.ReadAllText(Path.Combine(
            SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

    // Non-vacuity, first and deliberately. A reflection guard fails OPEN: a renamed namespace would leave every
    // case below passing for ever over an empty candidate set, and « found nothing » would read as « nothing
    // wrong ». The named verbs are the two ends of the range — the oldest and the newest.
    [Fact]
    public void The_Guard_Actually_Finds_The_Console_Verbs()
    {
        var names = Verbs().Select(NameOf).ToList();

        Assert.True(names.Count >= 10, $"Only {names.Count} verb(s) found — the reflection filter is wrong.");
        Assert.Contains("reset-admin-password", names);
        Assert.Contains("reprotect-secrets", names);
    }

    [Fact]
    public void Every_Console_Verb_Is_Dispatched_By_Program()
    {
        var program = ProgramSource();

        var missing = Verbs()
            .Where(v => !IsDispatched(program, v))
            .Select(v => $"{v.Name} ({NameOf(v)})")
            .ToList();

        Assert.True(missing.Count == 0,
            "These verbs exist but Program.cs does not dispatch them, so running one silently boots the WEB HOST "
            + "instead and reads to an operator as « the command did nothing »:" + Environment.NewLine
            + string.Join(Environment.NewLine, missing.Select(m => "  " + m)));
    }

    // Two verbs answering to one name means whichever branch is written first wins, silently.
    [Fact]
    public void No_Two_Verbs_Share_A_Name()
    {
        var duplicates = Verbs()
            .GroupBy(NameOf, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    // The executed red proof: the real check, run against a Program.cs with `reprotect-secrets`' branch removed,
    // so this class carries its own evidence rather than asking a reviewer to delete a line and watch.
    [Fact]
    public void The_Guard_Rejects_A_Verb_Whose_Dispatch_Branch_Is_Removed()
    {
        var program = ProgramSource();
        var withoutBranch = program.Replace("ReprotectSecretsCommand.", "SomethingElse.", StringComparison.Ordinal);

        Assert.NotEqual(program, withoutBranch);

        var missing = Verbs().Where(v => !IsDispatched(withoutBranch, v)).Select(v => v.Name).ToList();

        Assert.Contains("ReprotectSecretsCommand", missing);
    }
}
