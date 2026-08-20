using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>Every handler that persists a <c>LabWorkOrder</c> posts its caisse dépense through
/// <c>LabOrderCaisseExpense.PostIfDueAsync</c>.</b>
///
/// <para><b>Why a derived guard and not a behavioural test.</b> <c>LabWorkOrderCaisseExpenseTests</c> proves the
/// aggregate answers « une dépense est-elle due ? » correctly, over an order built in memory. That is exactly the
/// shape this guard exists for: the rule is right, and a call site simply does not ask it. The rule shipped wired
/// to the status transition alone, and the door it missed was the ordinary one — a bon arrives before the
/// laboratory's facture, so it is received with no coût and <b>edited</b> to enter it. That bon owed a dépense
/// with nothing left to post it, and no test over the aggregate could see it.</para>
///
/// <para>⚠️ <b>The failure is silent.</b> Nothing throws and nothing logs: the bon saves, the status is right, la
/// caisse is simply short by the price of a crown. It surfaces as a month's net being wrong, long after the
/// change that caused it.</para>
///
/// <para>The candidate set is derived from <b>what the sources actually do</b> — every file that calls a mutating
/// member of the aggregate — never from a list somebody remembered to keep up to date.</para>
/// </summary>
public class LabOrderCaisseExpenseCoverageTests
{
    /// <summary>
    /// The members that can leave an order owing a dépense. <c>SetStatus</c> is the arrival itself;
    /// <c>UpdateDetails</c> writes <c>Cost</c>, which is the other half of the debt.
    ///
    /// <para>⚠️ Anchored on the receiver's dot and on the opening paren, so a member merely *named* in prose or a
    /// controller action that happens to share the name is not mistaken for a call.</para>
    /// </summary>
    private static readonly string[] MutatorsThatCanCreateTheDebt = ["SetStatus", "UpdateDetails"];

    /// <summary>
    /// Files that call a mutator while being unable to owe anything, each with the reason it is sound.
    ///
    /// <para>Empty, and that is the finding: <c>CreateLabWorkOrderCommand</c> needs no exemption because it calls
    /// no mutator at all — a new bon is « Envoyé » by construction and cannot be created already received, so
    /// nothing can be owed at creation and the scan never reaches it.</para>
    ///
    /// <para>⚠️ Asserted in <b>both</b> directions, so a stale entry fails too: an exemption that no longer names
    /// a real calling file is a pre-approved hole standing open for whatever is written next.</para>
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal);

    private static string[] CallingFiles()
    {
        var root = SolutionSources.Root();
        var callsAMutator = new Regex(
            @"\.(?:" + string.Join('|', MutatorsThatCanCreateTheDebt) + @")\s*\(",
            RegexOptions.Compiled);

        return SolutionSources.CsFiles(root)
            .Where(path => path.Contains(Path.Combine("ClinicManagement.Application", "Features", "LabOrders")))
            .Where(path => callsAMutator.IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    // The guard would pass vacuously if the scan found nothing — a broken path or a renamed folder must fail
    // loudly rather than report every handler covered.
    [Fact]
    public void The_Scan_Finds_The_Handlers_It_Is_Meant_To_Check()
    {
        var files = CallingFiles();

        Assert.NotEmpty(files);
        Assert.Contains("UpdateLabWorkOrderStatusCommand.cs", files);
        Assert.Contains("UpdateLabWorkOrderCommand.cs", files);
    }

    [Fact]
    public void Every_Handler_That_Can_Owe_A_Depense_Posts_It()
    {
        var root = SolutionSources.Root();
        var byName = SolutionSources.CsFiles(root)
            .Where(path => path.Contains(Path.Combine("ClinicManagement.Application", "Features", "LabOrders")))
            .ToDictionary(path => Path.GetFileName(path)!, path => path, StringComparer.Ordinal);

        var missing = CallingFiles()
            .Where(name => !Exempt.ContainsKey(name))
            .Where(name => !File.ReadAllText(byName[name]).Contains(
                "LabOrderCaisseExpense.PostIfDueAsync", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These handlers mutate a LabWorkOrder in a way that can leave a caisse dépense owing, but never call "
            + "LabOrderCaisseExpense.PostIfDueAsync. La caisse would silently be short the price of the work: "
            + string.Join(", ", missing));
    }

    // A stale exemption is worse than none: it stands open for whatever is written next.
    [Fact]
    public void No_Exemption_Names_A_File_That_No_Longer_Calls_A_Mutator()
    {
        var callers = CallingFiles();

        var stale = Exempt.Keys.Where(name => !callers.Contains(name, StringComparer.Ordinal)).ToArray();

        Assert.True(
            stale.Length == 0,
            "These exemptions no longer name a file that mutates a LabWorkOrder — delete them: "
            + string.Join(", ", stale));
    }
}
