using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>A patient's refusal is asked in one place, and every enqueue path asks it.</b>
///
/// <para><b>What this exists to stop.</b> Recording a phone number used to enrol a patient into SMS/WhatsApp
/// with no way for the patient or the cabinet to exempt anybody. The fix is a tri-state column and a single
/// predicate, <c>Patient.AcceptsReminders</c> — and the way that fix fails in this repository is not that it is
/// written wrongly, it is that it is wired to <b>one</b> of the two enqueue paths. That is the shape recorded in
/// <c>TotpReplayCoverageTests</c> and again in <c>RecoveryCodeLoadingCoverageTests</c>: a correct helper on some
/// of its call sites, indistinguishable from a correct helper on all of them until somebody is messaged who
/// said no.</para>
///
/// <para>So the check is <b>derived from the sources</b> rather than from a list somebody remembers to extend.
/// Two properties, and each one alone would leave a real hole open:</para>
/// <list type="number">
///   <item><description>The consent enum's <c>Refused</c> member is compared in exactly one production file —
///   <c>Patient.cs</c>. A second <c>== Refused</c> written at a call site is a second definition of « may we
///   contact this person », and the two drift the first time the rule gains a state.</description></item>
///   <item><description><c>ReminderScheduler</c> loads the patient in exactly one method. Two enqueue paths
///   share <c>ReachabilityOfAsync</c> today; a third written against the repository directly would silently
///   skip the consent question, and this is what fails when it is.</description></item>
/// </list>
/// </summary>
public class ReminderConsentCoverageTests
{
    private const string RefusedMember = "PatientReminderConsent.Refused";
    private const string SchedulerFile = "ReminderScheduler.cs";
    private const string PatientRead = "_patients.GetByIdAsync(";

    /// <summary>
    /// The production files allowed to name <c>Refused</c>, each with the reason. Asserted <b>equal in both
    /// directions</b>, so a stale entry fails as loudly as a new violation — the house style, and the half that
    /// keeps an exemption from outliving the code it was written for.
    /// </summary>
    private static readonly Dictionary<string, string> MayCompareRefused = new(StringComparer.Ordinal)
    {
        ["Patient.cs"] =
            "The definition itself: `AcceptsReminders` is the single translation of the enum into a yes/no, and "
            + "every caller asks that property rather than the enum.",
    };

    [Fact]
    public void Only_The_Entity_Decides_What_A_Refusal_Means()
    {
        var comparing = ProductionSources()
            .Where(f => f.Source.Contains(RefusedMember, StringComparison.Ordinal))
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            MayCompareRefused.Keys.OrderBy(k => k, StringComparer.Ordinal),
            comparing);
    }

    /// <summary>
    /// ⚠️ <b>The load-bearing one.</b> Both enqueue paths must reach the patient through the one method that
    /// asks about consent. A third path that calls the repository itself would compile, pass every other test,
    /// and message somebody who refused.
    /// </summary>
    [Fact]
    public void The_Scheduler_Reads_A_Patient_In_Exactly_One_Place()
    {
        var source = SchedulerSource();
        var reads = CountOccurrences(source, PatientRead);

        Assert.True(
            reads == 1,
            $"{SchedulerFile} loads a patient {reads} time(s). It must be exactly once, inside "
            + "ReachabilityOfAsync — every enqueue path asks that method, so that consent and deliverability "
            + "are answered together and cannot disagree. A new read here is a path that skipped the consent "
            + "question.");
    }

    /// <summary>
    /// And that the one read really is the consent-aware one, rather than a phone-only check that happens to be
    /// alone in the file.
    /// </summary>
    [Fact]
    public void That_One_Place_Asks_About_Consent()
    {
        var source = SchedulerSource();

        Assert.Contains("AcceptsReminders", source, StringComparison.Ordinal);
        Assert.Contains("ReachabilityOfAsync", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Non-vacuity. A source scan fails <b>open</b>: a renamed field or a moved project leaves this class green
    /// for ever while checking nothing — how <c>SystemWideCallerCoverageTests</c>' console-verb branch matched
    /// nothing for two whole features.
    /// </summary>
    [Fact]
    public void The_Scan_Still_Finds_The_Sources_It_Describes()
    {
        var files = ProductionSources().Select(f => f.Name).ToList();

        Assert.True(files.Count > 200, $"Only {files.Count} production source file(s) found — the scan is blind.");
        Assert.Contains(SchedulerFile, files);
        Assert.Contains("Patient.cs", files);
    }

    private static string SchedulerSource() =>
        ProductionSources().Single(f => f.Name == SchedulerFile).Source;

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;

        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private static List<(string Name, string Source)> ProductionSources()
    {
        var root = SolutionSources.Root();
        var files = new List<(string, string)>();

        foreach (var file in SolutionSources.CsFiles(root))
        {
            // The test project is excluded: this class names the probe strings above, and a guard that fails on
            // its own description is a guard nobody can write.
            if (file.Contains($"{Path.DirectorySeparatorChar}ClinicManagement.UnitTests{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            files.Add((Path.GetFileName(file), File.ReadAllText(file)));
        }

        return files;
    }
}
