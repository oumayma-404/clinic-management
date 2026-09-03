using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>Every read of a <c>TreatmentPlan</c> that a write path uses must LOAD its acts' steps.</b>
///
/// <para><b>Why a derived guard and not a behavioural test.</b> <c>TreatmentPlanItemStepTests</c> proves the
/// aggregate advances, completes, reopens and refuses correctly — over a plan built in memory with its steps
/// already attached. That is exactly the shape this guard exists for: the aggregate is right, the <i>query</i>
/// that feeds it is not, and no mock-repository suite can tell the difference. It is
/// <c>RecoveryCodeLoadingCoverageTests</c>' lesson applied to the one collection this feature added.</para>
///
/// <para>⚠️ <b>The failure is silent, and it corrupts rather than merely hides.</b> <c>Steps</c> projects a
/// private backing list, there is no lazy loading and no <c>AutoInclude</c> in this solution — so an unloaded
/// collection is not stale, it is <b>empty</b>, and <c>HasSteps</c> answers <c>false</c>. A bridge two-thirds
/// finished would then take the step-less path: <c>MarkItemDone</c> would write <c>Done</c> straight onto the act
/// and <c>RecomputeStatusFromSteps</c> would return without touching it, so the devis would close with the
/// scellement never carried out — and the next save would persist that. No exception, no log line.</para>
///
/// <para>It is also why <c>TreatmentPlanItem.Status</c> is a <b>stored</b> column recomputed on write rather than
/// a property derived on read: a stored scalar is at worst out of date and <c>verify-schema</c>'s
/// <c>plan-step-status-agrees</c> sees that, whereas a derived one over an unloaded navigation is confidently
/// wrong with nothing to compare it against. This guard covers the write half — the one surface where the
/// collection genuinely has to be there.</para>
///
/// <para>The candidate set is derived from <b>what the sources actually do</b> — which repository reads load the
/// collection, and which files call a step-touching member — never from a list somebody remembered to update.</para>
/// </summary>
public class TreatmentStepLoadingCoverageTests
{
    private const string RepositoryFile = "TreatmentPlanRepository.cs";
    private const string RepositoryInterface = "ITreatmentPlanRepository";

    /// <summary>
    /// Files that mutate steps on a plan they obtained somewhere this guard cannot follow, each with the reason
    /// it is sound anyway.
    ///
    /// <para>⚠️ Asserted in <b>both</b> directions, so a stale entry fails too: an exemption naming a file that no
    /// longer exists is a pre-approved hole standing open for whatever is written next.</para>
    /// </summary>
    private static readonly Dictionary<string, string> ResolvesItsPlanElsewhere = new(StringComparer.Ordinal)
    {
        ["TreatmentPlan.cs"] = "The aggregate itself — it IS the loaded graph, not a caller of a read.",
        ["TreatmentPlanItem.cs"] = "The child that owns the collection.",
    };

    /// <summary>
    /// The members that only behave correctly over a <b>loaded</b> step collection. Every one of them either
    /// reads the backing list or decides a status from its contents, and both are wrong — differently — when it
    /// is empty.
    ///
    /// <para>⚠️ Anchored on the receiver's <b>dot</b> and the call's <b>parens</b>, for the reason the recovery-code
    /// guard states: without the dot, a controller action merely <i>named</i> <c>SetItemSteps</c> reads as a call
    /// on an aggregate the controller never loads; without the parens, a doc comment mentioning the member counts
    /// as a call.
    /// </para>
    /// </summary>
    private static readonly Regex TouchesTheSteps = new(
        @"\.\s*(SetItemSteps|MarkItemStepDone|UnmarkItemStep|MarkItemDone|UnmarkItemDone)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// A method declaration in the repository that returns a plan (or plans).
    ///
    /// <para>⚠️ The character class admits <c>&lt;</c> and <c>&gt;</c> deliberately. An earlier
    /// <c>Task&lt;[^&gt;]*TreatmentPlan[^&gt;]*&gt;</c> could not cross a nested generic, so it saw
    /// <c>Task&lt;TreatmentPlan?&gt;</c> and missed <c>Task&lt;PagedResult&lt;TreatmentPlan&gt;&gt;</c> and
    /// <c>Task&lt;IReadOnlyList&lt;TreatmentPlan&gt;&gt;</c> — i.e. it silently exonerated the two reads that
    /// serve the list screens. The guard's own tripwire is what caught it, which is the argument for having one.
    /// <c>(</c> stays out of the class so the match cannot run past the parameter list into the next method.</para>
    /// </summary>
    private static readonly Regex RepositoryRead = new(
        @"public\s+async\s+Task<[\w\s,<>?\[\]\.]*TreatmentPlan[\w\s,<>?\[\]\.]*>\s+(?<name>\w+)\s*\(",
        RegexOptions.Compiled);

    /// <summary>The include that actually brings the steps along.</summary>
    private static readonly Regex LoadsTheSteps = new(
        @"ThenInclude\s*\(\s*\w+\s*=>\s*\w+\.Steps\s*\)", RegexOptions.Compiled);

    // [FR-1] A stepped act's progress may never be decided over an unloaded collection.
    [Fact]
    public void Every_Write_Path_Loads_The_Steps_Of_The_Plan_It_Mutates()
    {
        var root = SolutionSources.Root();
        var files = SolutionSources.CsFiles(root).ToList();

        var repositoryPath = files.SingleOrDefault(f => Path.GetFileName(f) == RepositoryFile)
            ?? throw new InvalidOperationException(
                $"{RepositoryFile} not found. A guard that cannot find the repository asserts nothing.");
        var repositorySource = File.ReadAllText(repositoryPath);

        // Which reads exist, and which of them load the steps. Derived per method body rather than per file, so
        // a repository where SOME reads include the collection cannot exonerate the ones that do not.
        var reads = ReadsIn(repositorySource);

        // Tripwire: an empty declaration scan would silently exonerate every caller.
        Assert.True(
            reads.Count >= 3 && reads.Values.Count(loads => loads) >= 1,
            $"{RepositoryFile}: found {reads.Count} read(s) returning a TreatmentPlan, "
            + $"{reads.Values.Count(loads => loads)} of them loading Steps. The declaration scan is broken — a "
            + "guard that cannot see the repository's reads cannot hold anything to them.");

        var unloaded = new List<string>();
        var unresolved = new List<string>();
        var callers = 0;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (name == RepositoryFile)
            {
                continue;
            }

            // ⚠️ Production code only. A test fixture builds its plan in memory with the steps attached, which
            // is the correct and only way to unit-test the aggregate — so every one of them is a « caller this
            // guard cannot follow », and admitting them would mean either an exemption entry per test file
            // (a list that rots) or the guard being switched off. `ClinicCreationEntitlementTests` scopes itself
            // the same way and for the same reason: `new Clinic(` has 19 matches, 17 of them fixtures.
            if (file.Contains("ClinicManagement.UnitTests", StringComparison.Ordinal))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            if (!TouchesTheSteps.IsMatch(SolutionSources.WithoutComments(source)))
            {
                continue;
            }

            callers++;

            var called = reads.Keys.Where(read => CallsRead(source, read)).ToList();
            if (called.Count == 0)
            {
                if (!ResolvesItsPlanElsewhere.ContainsKey(name))
                {
                    unresolved.Add(name);
                }

                continue;
            }

            unloaded.AddRange(called.Where(read => !reads[read]).Select(read => $"{name} → {read}"));
        }

        // Tripwire: no callers at all means the member regex stopped matching, not that the product is clean.
        Assert.True(
            callers >= 3,
            $"Only {callers} file(s) appear to touch a plan's steps. The member scan is broken — the feature has "
            + "a domain, two commands and two dental-record handlers on this path.");

        Assert.True(
            unloaded.Count == 0,
            "These path(s) mutate a devis act's steps through a read that does NOT load them: "
            + string.Join(", ", unloaded.Order(StringComparer.Ordinal))
            + ". The collection will be empty, so HasSteps reads false and a half-finished treatment is written "
            + $"straight to « réalisé » with no error. Add `.ThenInclude(i => i.Steps)` to that read in "
            + $"{RepositoryFile}, or call one that already has it.");

        Assert.True(
            unresolved.Count == 0,
            "These path(s) mutate a devis act's steps without going through a repository read this guard can "
            + "see: "
            + string.Join(", ", unresolved.Order(StringComparer.Ordinal))
            + $". Either load the plan through {RepositoryInterface}, or record the file in "
            + $"{nameof(ResolvesItsPlanElsewhere)} with the reason its steps are loaded anyway.");

        // Both directions: a reason that no longer names a real file is a hole left open on purpose.
        var stale = ResolvesItsPlanElsewhere.Keys
            .Where(exempt => !files.Any(f => Path.GetFileName(f) == exempt))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"{nameof(ResolvesItsPlanElsewhere)} names file(s) that no longer exist: "
            + string.Join(", ", stale)
            + ". Remove the entry — a stale exemption pre-approves whatever takes that name next.");
    }

    /// <summary>
    /// Each plan-returning read in the repository, mapped to whether its body loads the steps. The body is taken
    /// as the text up to the next <c>public</c> declaration, which is enough here and needs no brace matching.
    /// </summary>
    private static Dictionary<string, bool> ReadsIn(string repositorySource)
    {
        var matches = RepositoryRead.Matches(repositorySource).ToList();
        var reads = new Dictionary<string, bool>(StringComparer.Ordinal);

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : repositorySource.Length;
            var body = repositorySource[start..end];

            reads[matches[i].Groups["name"].Value] = LoadsTheSteps.IsMatch(body);
        }

        return reads;
    }

    private static bool CallsRead(string source, string read) =>
        Regex.IsMatch(SolutionSources.WithoutComments(source), $@"\.\s*{Regex.Escape(read)}\s*\(");
}
