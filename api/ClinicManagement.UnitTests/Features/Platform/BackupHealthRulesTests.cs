using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// [server-loss-recovery Part 3] The verdict the console's status strip and the daily alert both read. Every
/// instant is a fixed literal — the rule takes « now » as a parameter, and a test reading the clock would agree with
/// a clock-dependent mistake by construction.
///
/// <para>The cases that matter most are the ones that must NOT read « À jour »: a missing record, a success with
/// no date, an old success. The 33 silent nights this exists for looked exactly like « no news ».</para>
/// </summary>
public class BackupHealthRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 7, 0, 0, DateTimeKind.Utc);

    private static NightlyBackupRecord Succeeded(DateTime at) => new("succeeded", null, at, at);

    private static WalArchiveRecord Archiving(DateTime lastArchived) => new(500, lastArchived, 0, null);

    // ── The nightly run ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void No_Recorded_Run_Is_Stale_Never_Ok()
    {
        var nightly = BackupHealthRules.EvaluateNightly(null, Now);

        Assert.Equal(BackupHealthRules.Stale, nightly.Verdict);
        Assert.Null(nightly.Outcome);
    }

    [Fact]
    public void A_Success_Last_Night_Is_Ok()
    {
        var nightly = BackupHealthRules.EvaluateNightly(Succeeded(Now.AddHours(-5)), Now);

        Assert.Equal(BackupHealthRules.Ok, nightly.Verdict);
        Assert.Equal("À jour", nightly.VerdictLabel);
    }

    [Fact]
    public void A_Success_Exactly_At_The_Threshold_Is_Still_Ok_And_One_Tick_Past_It_Is_Not()
    {
        Assert.Equal(BackupHealthRules.Ok,
            BackupHealthRules.EvaluateNightly(Succeeded(Now - BackupHealthRules.NightlyStaleAfter), Now).Verdict);
        Assert.Equal(BackupHealthRules.Stale,
            BackupHealthRules.EvaluateNightly(
                Succeeded(Now - BackupHealthRules.NightlyStaleAfter - TimeSpan.FromTicks(1)), Now).Verdict);
    }

    [Fact]
    public void A_Failed_Run_Is_Failing_And_Names_Its_Stage_And_Keeps_The_Last_Success()
    {
        var lastSuccess = Now.AddDays(-34);
        var nightly = BackupHealthRules.EvaluateNightly(
            new NightlyBackupRecord("failed", "objects", Now.AddHours(-5), lastSuccess), Now);

        Assert.Equal(BackupHealthRules.Failing, nightly.Verdict);
        Assert.Equal("En échec", nightly.VerdictLabel);
        Assert.Equal("objects", nightly.FailedStage);
        Assert.Equal(lastSuccess, nightly.LastSuccessAt);
    }

    // A file that says « succeeded » with no date proves nothing about WHEN.
    [Fact]
    public void A_Success_Without_A_Date_Is_Stale()
    {
        var nightly = BackupHealthRules.EvaluateNightly(new NightlyBackupRecord("succeeded", null, null, null), Now);

        Assert.Equal(BackupHealthRules.Stale, nightly.Verdict);
    }

    [Fact]
    public void An_Outcome_Nobody_Writes_Is_Not_Read_As_Success()
    {
        var nightly = BackupHealthRules.EvaluateNightly(
            new NightlyBackupRecord("running", null, Now, Now), Now);

        Assert.Equal(BackupHealthRules.Stale, nightly.Verdict);
    }

    [Fact]
    public void A_Blank_Stage_Is_Reported_As_None()
    {
        var nightly = BackupHealthRules.EvaluateNightly(
            new NightlyBackupRecord("succeeded", "  ", Now.AddHours(-1), Now.AddHours(-1)), Now);

        Assert.Null(nightly.FailedStage);
    }

    // ── The WAL stream ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Statistics_That_Could_Not_Be_Read_Are_Stale()
    {
        var wal = BackupHealthRules.EvaluateWal(null, Now);

        Assert.Equal(BackupHealthRules.Stale, wal.Verdict);
    }

    [Fact]
    public void A_Segment_Archived_Minutes_Ago_Is_Ok()
    {
        Assert.Equal(BackupHealthRules.Ok,
            BackupHealthRules.EvaluateWal(Archiving(Now.AddMinutes(-3)), Now).Verdict);
    }

    [Fact]
    public void An_Hour_Of_Silence_Is_Stale()
    {
        Assert.Equal(BackupHealthRules.Stale,
            BackupHealthRules.EvaluateWal(
                Archiving(Now - BackupHealthRules.WalStaleAfter - TimeSpan.FromTicks(1)), Now).Verdict);
    }

    [Fact]
    public void Never_Archived_Is_Stale()
    {
        Assert.Equal(BackupHealthRules.Stale,
            BackupHealthRules.EvaluateWal(new WalArchiveRecord(0, null, 0, null), Now).Verdict);
    }

    // The live failure mode: PostgreSQL is retrying one segment and pg_wal is growing on the server's disk.
    [Fact]
    public void A_Failure_After_The_Last_Success_Is_Failing()
    {
        var wal = BackupHealthRules.EvaluateWal(
            new WalArchiveRecord(500, Now.AddMinutes(-20), 3, Now.AddMinutes(-1)), Now);

        Assert.Equal(BackupHealthRules.Failing, wal.Verdict);
    }

    // `failed_count` is a lifetime total, not a streak: an old failure the archiver has since recovered from is
    // history, and alarming on it every morning would teach the vendor to ignore the alarm.
    [Fact]
    public void A_Failure_Already_Recovered_From_Is_Ok()
    {
        var wal = BackupHealthRules.EvaluateWal(
            new WalArchiveRecord(500, Now.AddMinutes(-2), 7, Now.AddDays(-3)), Now);

        Assert.Equal(BackupHealthRules.Ok, wal.Verdict);
        Assert.Equal(7, wal.FailedCount);
    }

    [Fact]
    public void Failures_With_No_Success_Ever_Are_Failing()
    {
        Assert.Equal(BackupHealthRules.Failing,
            BackupHealthRules.EvaluateWal(new WalArchiveRecord(0, null, 4, Now.AddMinutes(-1)), Now).Verdict);
    }

    // ── Overall ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Both_Healthy_Is_Ok()
    {
        var health = BackupHealthRules.Evaluate(
            new HostBackupSnapshot(Succeeded(Now.AddHours(-5)), Archiving(Now.AddMinutes(-2))), Now);

        Assert.Equal(BackupHealthRules.Ok, health.Verdict);
        Assert.Equal(Now, health.CheckedAt);
    }

    // The 2026-10-06 state of the live server: the WAL stream fine, the nightly run failing for weeks.
    [Fact]
    public void One_Copy_Failing_Makes_The_Whole_Verdict_Failing()
    {
        var health = BackupHealthRules.Evaluate(
            new HostBackupSnapshot(
                new NightlyBackupRecord("failed", "objects", Now.AddHours(-5), null),
                Archiving(Now.AddMinutes(-2))),
            Now);

        Assert.Equal(BackupHealthRules.Failing, health.Verdict);
        Assert.Equal(BackupHealthRules.Ok, health.WalArchive.Verdict);
    }

    [Fact]
    public void Failing_Outranks_Stale()
    {
        var health = BackupHealthRules.Evaluate(
            new HostBackupSnapshot(null, new WalArchiveRecord(500, Now.AddMinutes(-20), 1, Now.AddMinutes(-1))),
            Now);

        Assert.Equal(BackupHealthRules.Stale, health.Nightly.Verdict);
        Assert.Equal(BackupHealthRules.Failing, health.Verdict);
    }

    [Fact]
    public void Stale_Outranks_Ok()
    {
        var health = BackupHealthRules.Evaluate(new HostBackupSnapshot(null, Archiving(Now.AddMinutes(-2))), Now);

        Assert.Equal(BackupHealthRules.Stale, health.Verdict);
        Assert.Equal("En retard", health.VerdictLabel);
    }
}
