using System.Reflection;
using ClinicManagement.API.Maintenance;
using ClinicManagement.Application.Features.Messaging.Commands;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Common;
using MediatR;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// AC-9.3, held as a guard rather than as a convention: <b>a practice must have no web-facing path to its own WhatsApp
/// forfait</b>, and every vendor verb must actually be reachable from the command line.
///
/// <para><b>Both directions are derived, not listed.</b> The vendor commands are found by reflection over
/// <c>Features.Messaging.Commands</c> and the verbs by reflection over <c>API.Maintenance</c>, so a <i>third</i> vendor
/// command — or a fourth verb — is covered on the day it is written. A hand-written list could only ever fail on the
/// cases somebody remembered, which is never the new one. <c>SubscriptionVendorCommandReachabilityTests</c>' shape, and
/// its lessons.</para>
///
/// <para>The second half matters more than it looks. A verb whose branch is missing from <c>Program.cs</c> does not
/// fail: the argument falls through and the process <b>boots the web host instead</b>, so the operator sees an API
/// starting up rather than an error and concludes the verb « did nothing ». Nothing else in the build can see that.</para>
/// </summary>
public class MessagingVendorCommandReachabilityTests
{
    private static IReadOnlyList<Type> VendorCommands() =>
        typeof(GrantMessagingAllowanceCommand).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Namespace == typeof(GrantMessagingAllowanceCommand).Namespace)
            .Where(t => typeof(IBaseRequest).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<Type> VendorVerbs() =>
        typeof(MessagingGrantCommand).Assembly
            .GetTypes()
            // ⚠️ A `static class` is abstract AND sealed in metadata, and every console verb is one — so the ordinary
            // `IsAbstract: false` filter would match none of them and this guard would pass on an empty set.
            .Where(t => t is { IsClass: true, IsNested: false } && (!t.IsAbstract || t.IsSealed))
            .Where(t => t.Namespace == "ClinicManagement.API.Maintenance")
            .Where(t => t.Name.StartsWith("Messaging", StringComparison.Ordinal)
                        && t.Name.EndsWith("Command", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static string CommandNameOf(Type verb) =>
        (string)verb.GetField("CommandName", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    // [AC-9.3] No controller anywhere references a vendor messaging command. A practice able to raise its own forfait
    // would not have one, and the whole design of this half of the feature rests on there being no such route — the
    // console reaches it through its own wrapper, which stages the journal row in the same transaction.
    [Fact]
    public void No_Controller_Reaches_A_Vendor_Messaging_Command()
    {
        var root = SolutionSources.Root();
        var commands = VendorCommands();

        // A derived guard that found nothing would pass while checking nothing — the lesson
        // SystemWideCallerCoverageTests' console-verb branch cost this repo.
        Assert.Equal(2, commands.Count); // grant · cancel

        var controllers = SolutionSources.CsFiles(root)
            .Where(p => p.Contains(
                $"{Path.DirectorySeparatorChar}Controllers{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(controllers);

        var offenders = (from path in controllers
                         let source = File.ReadAllText(path)
                         from command in commands
                         where source.Contains(command.Name, StringComparison.Ordinal)
                         select $"{Path.GetRelativePath(root.FullName, path)} → {command.Name}")
            .OrderBy(o => o, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "AC-9.3: no HTTP path may change a cabinet's own messaging allowance, but:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => "  " + o)));
    }

    // [AC-9.1] The mirror: every verb is dispatched by Program.cs. A missing branch silently boots the web host with the
    // verb's own arguments, which reads to an operator as « the command did nothing ».
    [Fact]
    public void Every_Vendor_Messaging_Verb_Is_Dispatched_By_Program()
    {
        var verbs = VendorVerbs();
        Assert.Equal(3, verbs.Count); // grant · cancel · report

        var program = File.ReadAllText(Path.Combine(
            SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

        var missing = verbs
            .Where(v => !program.Contains($"{v.Name}.CommandName", StringComparison.Ordinal)
                        || !program.Contains($"{v.Name}.RunAsync(args)", StringComparison.Ordinal))
            .Select(v => v.Name)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These vendor verbs exist but Program.cs never dispatches them, so invoking one boots the web host "
            + $"instead: {string.Join(", ", missing)}");
    }

    // A derived guard that has never gone red is not yet a guard. Rather than asking a reviewer to delete a branch, this
    // feeds the same predicate a copy of Program.cs with one verb's dispatch stripped and asserts it flips.
    [Fact]
    public void The_Dispatch_Guard_Rejects_A_Verb_Whose_Branch_Is_Removed()
    {
        var program = File.ReadAllText(Path.Combine(
            SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

        Assert.Contains($"{nameof(MessagingReportCommand)}.RunAsync(args)", program);

        var stripped = program.Replace(
            $"{nameof(MessagingReportCommand)}.RunAsync(args)", "NoLongerDispatched()", StringComparison.Ordinal);

        Assert.DoesNotContain($"{nameof(MessagingReportCommand)}.RunAsync(args)", stripped);
    }

    // Every verb names a distinct command word in the `messaging-` family, so two cannot answer to the same argument
    // where the first branch would silently win.
    [Fact]
    public void Each_Verb_Has_Its_Own_Messaging_Command_Word()
    {
        var names = VendorVerbs().Select(CommandNameOf).ToList();

        Assert.All(names, n => Assert.StartsWith("messaging-", n, StringComparison.Ordinal));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ── AC-8.2's threshold is ONE figure ──────────────────────────────────────────────────────────────────────────
    //
    // « À moins de 10 % » is a boundary the vendor reads as exact, and two spellings of it is how a filter and its own
    // chip label come to disagree with neither screen looking wrong on its own. The predicate reads the constant; the
    // portfolio read serves the same constant to the console, which builds its label from it. This pins the serving half
    // — the SQL half is out of this suite's reach and is asserted by the console's own label being derived, not typed.
    [Fact]
    public void The_Near_Exhausted_Threshold_Is_A_Single_Named_Constant()
    {
        Assert.Equal(90, PlatformPortfolioFilter.MessagingNearExhaustedPercent);

        // The console must not retype it. It is served on every portfolio page, so the chip's « à moins de N % » is
        // 100 − this figure and cannot drift from the predicate that produced the rows.
        var root = SolutionSources.Root();
        var repository = File.ReadAllText(Path.Combine(
            root.FullName, "ClinicManagement.Infrastructure", "Repositories", "ClinicActivityRepository.cs"));

        Assert.Contains("PlatformPortfolioFilter.MessagingNearExhaustedPercent", repository);

        // And no literal 90 or 0.90 in the CODE: a hardcoded copy is the failure this test exists for.
        //
        // ⚠️ Comments are stripped first, the lesson `CnamClosedSetContractTests` records: the predicate's own docstring
        // explains why it is `× 100 ≥ × 90` rather than `≥ 0.90 ×`, so a scan over the raw file fires on the very prose
        // that documents the decision — and a guard that reddens on its own documentation gets deleted rather than fixed.
        var code = StripComments(repository);

        Assert.DoesNotContain("* 90", code);
        Assert.DoesNotContain("0.90", code);
    }

    /// <summary>Drops <c>//</c> line comments and <c>/* */</c> blocks, so a scan sees code rather than prose.</summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join(
            Environment.NewLine,
            withoutBlocks
                .Split('\n')
                .Select(line =>
                {
                    var comment = line.IndexOf("//", StringComparison.Ordinal);
                    return comment < 0 ? line : line[..comment];
                }));
    }
}
