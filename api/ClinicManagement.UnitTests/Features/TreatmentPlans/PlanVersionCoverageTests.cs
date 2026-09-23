using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// Every command that writes a treatment plan round-trips the version the user was editing.
///
/// <para>
/// ⚠️ <b>Five of them did not, and the one that mattered most was <c>Cancel</c></b> — the single irreversible
/// write on the aggregate, so a cancellation landed over a colleague's concurrent amendment with no 409 and no
/// trace, on a plan that could then never be recovered. <c>Complete</c>, <c>RecordInstallmentPayment</c>,
/// <c>ReviseTreatmentPlanInstallments</c> (which rewrites the <i>whole</i> échéancier) and
/// <c>SetTreatmentPlanItemOrder</c> were the other four, and the two payment-correction commands
/// (<c>VoidInstallmentPayment</c>, <c>SetInstallmentPaymentBanked</c>) declared none either — which is what
/// made their missing <c>when (ex is not ConflictException)</c> filters harmless and undetectable at once.
/// </para>
/// <para>
/// A <b>derived</b> scan rather than a list: the offender is always the command added next, and an
/// expectation somebody has to remember to extend is the defect one level up. It reads the folder, so a new
/// command file is covered the day it is written.
/// </para>
/// <para>
/// ⚠️ <c>0</c> still means « not supplied » and skips the check — the solution-wide convention. This asserts
/// the <i>seam exists</i>, never that a caller uses it.
/// </para>
/// </summary>
public class PlanVersionCoverageTests
{
    /// <summary>
    /// Commands that legitimately declare no expected version, each with the reason it is exempt.
    ///
    /// <para>
    /// ⚠️ This list may only shrink. Every entry is a command that does not write a plan that already exists,
    /// so there is no user-held copy for a version to protect — an entry added for any other reason is the
    /// allow-list-that-grows failure this guard is written to avoid.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["CreateTreatmentPlanCommand"] = "inserts the plan — there is no prior version to check against",
        ["StartTreatmentCommand"] = "inserts the plan (« Suivre ce traitement »)",
        ["ContinueRecordedActCommand"] = "inserts the plan, from a fiche already recorded",
        ["DeleteTreatmentPlanCommand"] = "Draft with no work only, and the row is removed rather than written",
        ["AcceptTreatmentPlanCommand"] = "legacy drafts only; superseded by IssueDevisCommand, which is versioned",
        ["CollectOnTreatmentCommand"] = "driven by the fiche's own save, which carries the fiche's version",
        ["DiscardBookingPlanCommand"] = "only a plan minutes old that no visit books; delegates to Delete / Cancel",
        ["DuplicateTreatmentPlanCommand"] = "inserts a copy as a new Draft; the source is read and never written",
        ["TreatmentPlanItemPricing"] = "not a command — shared pricing resolution",
        ["TreatmentPlanStepProtocol"] = "not a command — shared protocol application",
    };

    [Fact]
    public void Every_Plan_Writing_Command_Declares_An_Expected_Version()
    {
        var folder = Path.Combine(
            SolutionRoot(), "ClinicManagement.Application", "Features", "TreatmentPlans", "Commands");

        Assert.True(Directory.Exists(folder), $"Command folder not found: {folder}");

        var files = Directory.GetFiles(folder, "*.cs");
        Assert.NotEmpty(files);

        var missing = new List<string>();
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (Exempt.ContainsKey(name))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            if (!source.Contains("SetExpectedVersion"))
            {
                missing.Add(name);
            }
        }

        Assert.True(
            missing.Count == 0,
            "These treatment-plan commands write a plan without declaring the version the user was editing, "
            + "so a concurrent edit is overwritten with no 409 and no trace: "
            + string.Join(", ", missing.OrderBy(n => n))
            + ". Add `_unitOfWork.SetExpectedVersion(plan, request.Version)` before the save, or record the "
            + "command in this test's `Exempt` map with the reason it writes no existing plan.");
    }

    /// <summary>
    /// The red proof: an exemption that no longer names a real file is a silent hole, because the entry keeps
    /// excusing a command that has been renamed while the new name goes unchecked.
    /// </summary>
    [Fact]
    public void Every_Exemption_Still_Names_A_File_That_Exists()
    {
        var folder = Path.Combine(
            SolutionRoot(), "ClinicManagement.Application", "Features", "TreatmentPlans", "Commands");

        var present = Directory.GetFiles(folder, "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet();

        var stale = Exempt.Keys.Where(k => !present.Contains(k)).OrderBy(k => k).ToList();

        Assert.True(
            stale.Count == 0,
            "These exemptions name no file — the command was renamed or deleted, so the entry now excuses "
            + "nothing while the new name goes unchecked: " + string.Join(", ", stale));
    }

    /// <summary>
    /// Both money-correction handlers must keep <c>when (ex is not ConflictException)</c> on their catch-all,
    /// or the 409 the versions above now make reachable is flattened back into a generic French sentence.
    /// </summary>
    [Fact]
    public void No_Plan_Command_Swallows_A_Conflict_In_Its_Catch_All()
    {
        var folder = Path.Combine(
            SolutionRoot(), "ClinicManagement.Application", "Features", "TreatmentPlans", "Commands");

        // A catch-all that RETURNS a Result must be filtered. A log-only post-commit catch deliberately is
        // not — but there is none in this folder, so any bare `catch (Exception` here is the defect.
        var bareCatchAll = new Regex(@"catch\s*\(\s*Exception[^)]*\)\s*(?!\s*when)", RegexOptions.Compiled);

        // The red proof: the pattern must match the literal it forbids, or a guard that checks nothing passes.
        Assert.Matches(bareCatchAll, "catch (Exception)\n{\n    return Result.Failure(\"…\");\n}");
        Assert.DoesNotMatch(bareCatchAll, "catch (Exception ex) when (ex is not ConflictException)");

        var offenders = Directory.GetFiles(folder, "*.cs")
            .Where(f => bareCatchAll.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These handlers flatten a 409 into their generic failure sentence, so the concurrency check "
            + "detects nothing a user can act on: " + string.Join(", ", offenders));
    }

    /// <inheritdoc cref="ClinicManagement.UnitTests.Domain.FollowedTreatmentLifecycleTests"/>
    /// <remarks>
    /// <c>[CallerFilePath]</c>, not <c>AppContext.BaseDirectory</c>: the suite is routinely built to a scratch
    /// <c>BaseOutputPath</c> outside the repository. It throws rather than skipping — a derived guard that
    /// silently scans nothing is worse than no guard at all.
    /// </remarks>
    private static string SolutionRoot([CallerFilePath] string thisFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClinicManagement.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                $"ClinicManagement.sln not found above '{thisFile}' — this guard would otherwise scan nothing "
                + "and pass, which is indistinguishable from finding no offenders.");
        }

        return directory.FullName;
    }
}
