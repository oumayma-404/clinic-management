using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>Every place that verifies a one-time code also spends it.</b>
///
/// <para><b>Why this is a guard and not a code review.</b> <c>ITotpReplayGuard</c> was written correctly, tested
/// correctly, registered correctly — and wired to <b>one</b> of the six places that call
/// <c>ITotpService.VerifyCode</c>. RFC 6238 § 5.2 forbids accepting a code twice, so for the other five a code
/// captured once stayed replayable for the rest of its ~90-second window. The three that mattered were the
/// <b>vendor console login</b> (the highest-privileged surface in the product), <b>step-up re-authentication</b>
/// (which gates the whole-clinic archive export and user management) and <b>ManageTotpCommands</b> (which is
/// « regenerate my recovery codes » and « disable my second factor » — the two operations that dismantle the
/// factor itself).</para>
///
/// <para>This is the repository's own « fixes don't propagate » shape, in the security layer. A correct helper
/// wired to one call site is indistinguishable from a correct helper wired to all of them, in every way except
/// the one that matters — so the check has to be <b>derived from the call sites</b> rather than from a list
/// somebody remembers to extend. A seventh verification site written next month is covered on the day it is
/// written, which is the property an <c>[InlineData]</c> table of today's sites cannot have.</para>
///
/// <para>⚠️ <b>Granularity is the FILE, deliberately.</b> Proving that a particular <c>TryConsume</c> guards a
/// particular <c>VerifyCode</c> means parsing C# and following control flow. At file granularity the check is
/// exact enough to have caught all five real misses and cheap enough to never produce a false one — and the
/// verification of a code is a small, self-contained thing that does not get split across files in this
/// codebase. If it ever does, tighten this rather than exempting it.</para>
/// </summary>
public class TotpReplayCoverageTests
{
    private const string Verify = ".VerifyCode(";
    private const string Consume = "TryConsume(";

    /// <summary>
    /// The files that verify a code and legitimately do <b>not</b> spend it, each with the reason. Asserted
    /// <b>equal in both directions</b>, so a stale entry fails as loudly as a new violation — the house style,
    /// and the half that stops an exemption outliving the code it was written for.
    ///
    /// <para>Both entries are <b>enrolment</b>, and enrolment is the one case where replay buys nothing: the
    /// code proves possession of a secret the server has just generated and which is not yet active on the
    /// account. There is no earlier presentation of it to capture, and the operation completes exactly once —
    /// the second call finds the account already enrolled and is refused on that ground instead.</para>
    /// </summary>
    private static readonly Dictionary<string, string> AllowedByDesign = new(StringComparer.Ordinal)
    {
        ["EnrolTotpCommand.cs"] =
            "Enrolment: the code proves possession of a not-yet-active secret the server just issued, so there "
            + "is no prior presentation to replay, and a second call is refused as already-enrolled.",
        ["EnrolPlatformTotpCommand.cs"] =
            "Console enrolment — the same argument as EnrolTotpCommand.cs.",
    };

    [Fact]
    public void Every_Code_Verification_Also_Spends_The_Code()
    {
        var unguarded = VerifyingFiles()
            .Where(f => !f.Source.Contains(Consume, StringComparison.Ordinal))
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            AllowedByDesign.Keys.OrderBy(k => k, StringComparer.Ordinal),
            unguarded);
    }

    /// <summary>
    /// Non-vacuity. A source scan fails <b>open</b>: a moved folder, a renamed project or a changed method name
    /// would leave this class green for ever while checking nothing — which is exactly how
    /// <c>SystemWideCallerCoverageTests</c>' console-verb branch matched nothing for two whole features.
    /// </summary>
    [Fact]
    public void The_Scan_Still_Finds_The_Verification_Sites()
    {
        var files = VerifyingFiles().Select(f => f.Name).Distinct(StringComparer.Ordinal).ToList();

        Assert.True(
            files.Count >= 5,
            $"Only {files.Count} file(s) call {Verify} — the scan has stopped seeing the solution's sources.");

        Assert.Contains("LoginCommand.cs", files);
        Assert.Contains("PlatformLoginCommand.cs", files);
    }

    /// <summary>
    /// And that the guarded side is real rather than an artefact of matching nothing: the three sign-in and
    /// re-authentication paths must all be spending codes.
    /// </summary>
    [Fact]
    public void The_Sites_That_Gate_Access_Do_Spend_The_Code()
    {
        var guarded = VerifyingFiles()
            .Where(f => f.Source.Contains(Consume, StringComparison.Ordinal))
            .Select(f => f.Name)
            .ToList();

        Assert.Contains("LoginCommand.cs", guarded);
        Assert.Contains("PlatformLoginCommand.cs", guarded);
        Assert.Contains("StepUpCommand.cs", guarded);
        Assert.Contains("ManageTotpCommands.cs", guarded);
    }

    private static IEnumerable<(string Name, string Source)> VerifyingFiles()
    {
        var root = SolutionSources.Root();

        foreach (var file in SolutionSources.CsFiles(root))
        {
            // The test project is excluded: this class names the probe strings above, and a guard that fails on
            // its own description is a guard nobody can write.
            if (file.Contains($"{Path.DirectorySeparatorChar}ClinicManagement.UnitTests{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            if (source.Contains(Verify, StringComparison.Ordinal))
            {
                yield return (Path.GetFileName(file), source);
            }
        }
    }
}
