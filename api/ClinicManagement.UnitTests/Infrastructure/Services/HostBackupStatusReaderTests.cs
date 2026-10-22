using System.Text.RegularExpressions;
using ClinicManagement.Infrastructure.Services;
using ClinicManagement.UnitTests.Common;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// [server-loss-recovery Part 3] The C# half of a two-language contract: <c>deploy/backup/backup.sh</c> writes
/// <c>backup.status</c> and <see cref="HostBackupStatusReader.Parse"/> reads it. A key renamed on one side is not an
/// error anywhere — the parser simply finds no outcome and the console reads « En retard » for ever — so the
/// load-bearing case derives the keys from the script itself rather than restating them.
/// </summary>
public class HostBackupStatusReaderTests
{
    [Fact]
    public void A_Successful_Run_Parses_Exactly_As_The_Script_Writes_It()
    {
        var record = HostBackupStatusReader.Parse(new[]
        {
            "run=20261007T020000Z",
            "outcome=succeeded",
            "stage=",
            "finished=2026-10-07T02:03:41Z",
            "lastSuccess=2026-10-07T02:03:41Z",
        });

        Assert.NotNull(record);
        Assert.Equal("succeeded", record!.Outcome);
        Assert.Null(record.FailedStage);
        Assert.Equal(new DateTime(2026, 10, 7, 2, 3, 41, DateTimeKind.Utc), record.LastSuccessUtc);
        Assert.Equal(DateTimeKind.Utc, record.LastSuccessUtc!.Value.Kind);
    }

    [Fact]
    public void A_Failed_Run_Keeps_Its_Stage_And_The_Previous_Success()
    {
        var record = HostBackupStatusReader.Parse(new[]
        {
            "run=20261007T020000Z",
            "outcome=failed",
            "stage=upload",
            "finished=2026-10-07T02:01:05Z",
            "lastSuccess=2026-10-05T02:02:10Z",
        });

        Assert.Equal("failed", record!.Outcome);
        Assert.Equal("upload", record.FailedStage);
        Assert.Equal(new DateTime(2026, 10, 5, 2, 2, 10, DateTimeKind.Utc), record.LastSuccessUtc);
    }

    // A failure on the very first run has no success to carry over — an empty value, never a made-up date.
    [Fact]
    public void An_Empty_Last_Success_Is_Null()
    {
        var record = HostBackupStatusReader.Parse(new[] { "outcome=failed", "stage=remote", "lastSuccess=" });

        Assert.Null(record!.LastSuccessUtc);
    }

    // Half a file, a truncated write, or someone's notes: no outcome means no record, which the rules grade Stale.
    [Theory]
    [InlineData]
    [InlineData("garbage")]
    [InlineData("run=20261007T020000Z", "stage=dump")]
    [InlineData("outcome=")]
    public void Lines_Without_An_Outcome_Are_No_Record(params string[] lines)
    {
        Assert.Null(HostBackupStatusReader.Parse(lines));
    }

    [Fact]
    public void An_Unreadable_Instant_Is_Null_Rather_Than_A_Guess()
    {
        var record = HostBackupStatusReader.Parse(new[] { "outcome=succeeded", "lastSuccess=hier soir" });

        Assert.Null(record!.LastSuccessUtc);
    }

    // ⚠️ The contract guard. Every key the parser reads must be one the script writes, and the instant format the
    // script uses must be the one the parser accepts — read off backup.sh, not restated here.
    [Fact]
    public void Every_Key_The_Parser_Reads_Is_A_Key_The_Backup_Script_Writes()
    {
        var script = File.ReadAllText(Path.Combine(
            SolutionSources.Root().Parent!.FullName, "deploy", "backup", "backup.sh"));

        var written = Regex.Matches(script, @"echo ""(\w+)=\$\{")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Non-vacuity: a regex that stopped matching would make every assertion below pass on an empty set.
        Assert.True(written.Count >= 5, $"Found only [{string.Join(", ", written)}] in backup.sh's write_status.");

        foreach (var key in new[] { "outcome", "stage", "finished", "lastSuccess" })
        {
            Assert.Contains(key, written);
        }

        // The instant format: `date -u +%Y-%m-%dT%H:%M:%SZ`, which ParseInstant reads as yyyy-MM-ddTHH:mm:ssZ.
        Assert.Contains("date -u +%Y-%m-%dT%H:%M:%SZ", script);
    }
}
