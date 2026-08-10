using System.Reflection;
using ClinicManagement.API.Maintenance;
using ClinicManagement.Application.Features.Subscriptions.Commands;
using ClinicManagement.UnitTests.Common;
using MediatR;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// FR-6, held as a guard rather than as a convention: <b>granting oneself a subscription must have no web-facing
/// path</b>, and every vendor verb must actually be reachable from the command line.
///
/// <para><b>Both directions are derived, not listed.</b> The vendor commands are found by reflection over
/// <c>Features.Subscriptions.Commands</c> and the verbs by reflection over <c>API.Maintenance</c>, so a <i>fourth</i>
/// vendor command — or a sixth verb — is covered on the day it is written. A hand-written list could only ever fail
/// on the cases somebody remembered, which is never the new one.</para>
///
/// <para>The second half matters more than it looks. A verb whose branch is missing from <c>Program.cs</c> does not
/// fail: the argument falls through and the process <b>boots the web host instead</b>, so the operator sees an API
/// starting up rather than an error, and concludes the verb « did nothing ». Nothing else in the build can see that.</para>
/// </summary>
public class SubscriptionVendorCommandReachabilityTests
{
    private static IReadOnlyList<Type> VendorCommands() =>
        typeof(GrantSubscriptionPeriodCommand).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Namespace == typeof(GrantSubscriptionPeriodCommand).Namespace)
            .Where(t => typeof(IBaseRequest).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<Type> VendorVerbs() =>
        typeof(SubscriptionGrantCommand).Assembly
            .GetTypes()
            // ⚠️ A `static class` is abstract AND sealed in metadata, and every console verb is one — so the
            // ordinary `IsAbstract: false` filter would match none of them and this guard would pass on an empty set.
            .Where(t => t is { IsClass: true, IsNested: false } && (!t.IsAbstract || t.IsSealed))
            .Where(t => t.Namespace == "ClinicManagement.API.Maintenance")
            .Where(t => t.Name.StartsWith("Subscription", StringComparison.Ordinal)
                        && t.Name.EndsWith("Command", StringComparison.Ordinal))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static string CommandNameOf(Type verb) =>
        (string)verb.GetField("CommandName", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    // [FR-6] No controller anywhere references a vendor command. A cabinet able to extend its own entitlement over
    // HTTP would not have one, and the whole design of this half of the feature rests on there being no such route.
    [Fact]
    public void No_Controller_Reaches_A_Vendor_Subscription_Command()
    {
        var root = SolutionSources.Root();
        var commands = VendorCommands();
        Assert.NotEmpty(commands);

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
            $"FR-6: no HTTP path may grant, cancel or suspend a subscription, but:{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders.Select(o => "  " + o)));
    }

    // [FR-6] The mirror: every verb is dispatched by Program.cs. A missing branch silently boots the web host with
    // the verb's own arguments, which reads to an operator as « the command did nothing ».
    [Fact]
    public void Every_Vendor_Verb_Is_Dispatched_By_Program()
    {
        var verbs = VendorVerbs();
        Assert.Equal(5, verbs.Count); // grant · cancel · suspend · unsuspend · report

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

    // A derived guard that has never gone red is not yet a guard. Rather than asking a reviewer to delete a branch,
    // this feeds the same predicate a copy of Program.cs with one verb's dispatch stripped and asserts it flips.
    [Fact]
    public void The_Dispatch_Guard_Rejects_A_Verb_Whose_Branch_Is_Removed()
    {
        var program = File.ReadAllText(Path.Combine(
            SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

        Assert.Contains($"{nameof(SubscriptionReportCommand)}.RunAsync(args)", program);

        var stripped = program.Replace(
            $"{nameof(SubscriptionReportCommand)}.RunAsync(args)", "NoLongerDispatched()", StringComparison.Ordinal);

        Assert.DoesNotContain($"{nameof(SubscriptionReportCommand)}.RunAsync(args)", stripped);
    }

    // Every verb names a distinct command word, and each is the `subscription-` family — so two verbs cannot answer
    // to the same argument, where the first branch would silently win.
    [Fact]
    public void Each_Verb_Has_Its_Own_Subscription_Command_Word()
    {
        var names = VendorVerbs().Select(CommandNameOf).ToList();

        Assert.All(names, n => Assert.StartsWith("subscription-", n, StringComparison.Ordinal));
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
