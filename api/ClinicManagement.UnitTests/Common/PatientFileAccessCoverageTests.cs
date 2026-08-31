using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// <b>Every path that streams a patient's stored content records that it did.</b>
///
/// <para><b>Why this is a guard and not a code review.</b> The ledger recorded Insert/Update/Delete and nothing
/// else, so the product could not answer « qui a sorti la radiographie de ce patient ? » — the only question
/// that means anything against a colleague who legitimately <i>has</i> access. When that was fixed there were
/// already <b>two</b> doors onto the same bytes, <c>DownloadPatientFileQuery</c> and
/// <c>DownloadPatientFilePreviewQuery</c>, and auditing one would have left the other silent while every screen
/// and every test reported success. That is this repository's own « fixes don't propagate » shape, which the
/// audit found thirteen times — including, twice, in the security layer itself.</para>
///
/// <para><b>Derived from the call sites.</b> The rule is « a handler in <c>Features/Files/Queries</c> that calls
/// <c>IFileStorage.DownloadAsync</c> must also call the access ledger », read off the sources, so a third door
/// written next month is covered on the day it is written — the property an <c>[InlineData]</c> table of today's
/// two queries cannot have.</para>
///
/// <para>⚠️ <b>File granularity, deliberately</b>, on <c>TotpReplayCoverageTests</c>' reasoning: proving that a
/// particular ledger call guards a particular stream means parsing C# and following control flow, while at file
/// granularity the check is exact enough to have caught the real miss and cheap enough never to produce a false
/// one. Streaming a patient's file is a small, self-contained thing that is not split across files here.</para>
/// </summary>
public class PatientFileAccessCoverageTests
{
    private const string Streams = "_fileStorage.DownloadAsync";
    private const string Records = "PatientRecordAccessLedger.RecordAsync";

    /// <summary>
    /// Handlers that stream a file and legitimately record nothing, each with the reason. Asserted <b>equal in
    /// both directions</b>, so a stale entry fails as loudly as a new gap — the house style.
    /// </summary>
    private static readonly Dictionary<string, string> NotAPatientRecordByDesign = new(StringComparer.Ordinal)
    {
        // Kept empty on purpose: every reader in this folder today is a patient's own content. An entry here
        // must name a file that streams something belonging to the CABINET rather than to a patient — a logo,
        // a practitioner's cachet — and those live outside Features/Files/Queries.
    };

    [Fact]
    public void Every_Patient_File_Stream_Is_Recorded()
    {
        var unrecorded = StreamingHandlers()
            .Where(f => !f.Source.Contains(Records, StringComparison.Ordinal))
            .Select(f => f.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            NotAPatientRecordByDesign.Keys.OrderBy(k => k, StringComparer.Ordinal),
            unrecorded);
    }

    /// <summary>
    /// Non-vacuity. A source scan fails <b>open</b>: a moved folder or a renamed field would leave this class
    /// green for ever while checking nothing — how <c>SystemWideCallerCoverageTests</c>' console-verb branch
    /// matched nothing for two whole features.
    /// </summary>
    [Fact]
    public void The_Scan_Still_Finds_Both_Doors()
    {
        var found = StreamingHandlers().Select(f => f.Name).ToList();

        Assert.True(found.Count >= 2, $"Only {found.Count} streaming handler(s) found — the scan has drifted.");
        Assert.Contains("DownloadPatientFileQuery.cs", found);
        Assert.Contains("DownloadPatientFilePreviewQuery.cs", found);
    }

    private static IEnumerable<(string Name, string Source)> StreamingHandlers()
    {
        var queries = Path.Combine(
            SolutionSources.Root().FullName,
            "ClinicManagement.Application", "Features", "Files", "Queries");

        if (!Directory.Exists(queries))
        {
            throw new DirectoryNotFoundException(
                $"The file-query folder was not found at '{queries}'. This guard cannot silently pass: "
                + "fix the path rather than leaving it scanning nothing.");
        }

        foreach (var file in Directory.EnumerateFiles(queries, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            if (source.Contains(Streams, StringComparison.Ordinal))
            {
                yield return (Path.GetFileName(file), source);
            }
        }
    }
}
