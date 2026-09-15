using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The startup backfills are owed by every profile, and they are written TWICE — once in the synchronous block in
/// <c>Program.cs</c> (<c>RunsStartupBackfills</c>) and once in <c>DeferredStartupService</c>, which a profile that
/// defers its migrations runs INSTEAD. Nothing in the compiler relates the two lists.
///
/// <para><b>This is the repo's dominant defect shape, at the startup layer.</b> Twice a backfill was added to the
/// synchronous path alone and silently never ran on <c>SelfHostedLan</c>: the clinic-admin backfill (a LAN clinic
/// created before onboarding assigned one stayed permanently without the account that creates staff, resets
/// passwords and reads the audit log), then <c>GoogleTokenProtectionBackfill</c> — whose omission stopped Google
/// Calendar push for any clinic connected before the protected column existed, because
/// <c>GoogleCalendarSyncService</c> reads <c>GoogleRefreshTokenProtected</c> with <b>no fallback</b> to the
/// plaintext column. Neither raised anything anywhere. Neither was visible to a unit test: they are wiring, not
/// behaviour, and the deferred path's own comment claimed the first omission had been deliberate.</para>
///
/// <para><b>Derived, never a hand-kept list.</b> Both sets are read out of source, so a fourth backfill is covered
/// the day it is written rather than the day somebody remembers this file exists. The scan is deliberately blunt —
/// a resolved <c>I…Backfill</c>/<c>I…Seeder</c>, or a call on a static <c>…Backfill</c> — because a guard that
/// tries to understand the code is a guard that reformatting can fool.</para>
///
/// <para>⚠️ The assertion is ONE-directional: the deferred path must cover everything the synchronous one does,
/// not the reverse. <c>DeferredStartupService</c> legitimately owns work with no synchronous twin (the
/// pre-migration backup), because a profile that does not defer has no service-start timeout to protect.</para>
/// </summary>
public class StartupBackfillCoverageTests
{
    /// <summary>
    /// What counts as a startup backfill invocation — the two shapes the paths actually use. The captured name is
    /// the identity compared across the two files.
    /// </summary>
    private static readonly Regex BackfillInvocation = new(
        @"GetRequiredService<\s*(?<name>I\w*(?:Backfill|Seeder))\s*>|(?<name>\w*Backfill)\s*\.\s*RunAsync",
        RegexOptions.Compiled);

    private const string DeferredService = "DeferredStartupService.cs";

    [Fact]
    public void Every_synchronous_startup_backfill_also_runs_on_the_deferred_path()
    {
        var synchronous = BackfillsIn(SynchronousBlock());
        var deferred = BackfillsIn(Read("ClinicManagement.API", "Startup", DeferredService));

        Assert.True(
            synchronous.Count > 0,
            "This guard found no backfill at all in Program.cs's RunsStartupBackfills block, so it is matching "
            + "nothing and would pass however broken the wiring became. Fix the scan, not the assertion.");

        var missing = synchronous.Except(deferred).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.True(
            missing.Count == 0,
            "A profile that defers its migrations runs DeferredStartupService INSTEAD of the synchronous block, so "
            + "a backfill present in one and absent from the other never runs on that deployment at all — and it "
            + "fails silently, because a backfill has no surface to report on. Add it to "
            + "DeferredStartupService.InitializeAsync. Missing there: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_deferred_path_runs_the_google_token_backfill()
    {
        // Named on its own because it is the one that shipped broken, and because the guard above would go quiet if
        // a future failure were "fixed" by deleting the synchronous call rather than adding the deferred one.
        Assert.Contains(
            "GoogleTokenProtectionBackfill",
            BackfillsIn(Read("ClinicManagement.API", "Startup", DeferredService)));
    }

    private static HashSet<string> BackfillsIn(string source) =>
        BackfillInvocation.Matches(source)
            .Select(m => m.Groups["name"].Value)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The body of <c>Program.cs</c>'s <c>if (profile.RunsStartupBackfills)</c> block — not the whole file, so an
    /// unrelated seeder resolved elsewhere in 1 400 lines cannot be mistaken for a startup backfill and demand a
    /// deferred twin that makes no sense.
    /// </summary>
    private static string SynchronousBlock()
    {
        var program = Read("ClinicManagement.API", "Program.cs");
        var start = program.IndexOf("if (profile.RunsStartupBackfills)", StringComparison.Ordinal);

        Assert.True(
            start >= 0,
            "This guard locates the synchronous backfill block by that exact condition. If it was renamed, point "
            + "the guard at the new one — leaving it unable to find the block makes it scan nothing and pass.");

        // Ends where the job scheduling begins. Matching the closing brace of the migration lock's lambda is
        // brittle; a generous window is safe because everything past the block resolves no backfill.
        var end = program.IndexOf("// Schedule background jobs", start, StringComparison.Ordinal);
        return end > start ? program[start..end] : program[start..];
    }

    private static string Read(params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { SolutionRoot() }.Concat(relativeParts).ToArray()));

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
                $"ClinicManagement.sln not found above '{thisFile}' — this guard would otherwise scan nothing and "
                + "pass, which is indistinguishable from finding no offenders.");
        }

        return directory.FullName;
    }
}
