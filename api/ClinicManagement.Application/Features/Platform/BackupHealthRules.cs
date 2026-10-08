using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform.Dtos;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// The one rule for « are the off-site copies healthy? » (<c>server-loss-recovery</c> Part 3), read by the console's
/// status strip and by the daily alert job, so the screen and the e-mail cannot disagree about the same night.
///
/// <para><b>Three verdicts, never two.</b> <c>Failing</c> — the last attempt failed, act now. <c>Stale</c> — nothing
/// failed, but nothing has been <i>proven</i> recently either: the run has not happened, or its record is missing,
/// or the archiver has gone quiet. A missing record is <b>never</b> <c>Ok</c>: the 33 silent nights this exists for
/// looked exactly like « no news ».</para>
///
/// <para>Pure — snapshot and instant in, verdicts out — so every boundary is testable with no file and no
/// database.</para>
/// </summary>
public static class BackupHealthRules
{
    public const string Ok = "Ok";
    public const string Stale = "Stale";
    public const string Failing = "Failing";

    /// <summary>
    /// A nightly run older than this is late. A day plus two hours: the cron is daily, and a run that takes a while
    /// or a container restarted mid-evening must not raise an alarm that a second look would cancel.
    /// </summary>
    public static readonly TimeSpan NightlyStaleAfter = TimeSpan.FromHours(26);

    /// <summary>
    /// WAL older than this is late. `archive_timeout` forces a segment every 5 minutes whenever anything was
    /// written, and the hosted API writes every minute (its own scheduled jobs), so an hour of silence means the
    /// stream has stopped rather than that nothing happened.
    /// </summary>
    public static readonly TimeSpan WalStaleAfter = TimeSpan.FromHours(1);

    public static PlatformBackupHealthDto Evaluate(HostBackupSnapshot snapshot, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var nightly = EvaluateNightly(snapshot.Nightly, nowUtc);
        var wal = EvaluateWal(snapshot.WalArchive, nowUtc);
        var overall = Worse(nightly.Verdict, wal.Verdict);

        return new PlatformBackupHealthDto(overall, Label(overall), nowUtc, nightly, wal);
    }

    public static PlatformNightlyBackupDto EvaluateNightly(NightlyBackupRecord? record, DateTime nowUtc)
    {
        if (record is null)
        {
            return new PlatformNightlyBackupDto(Stale, Label(Stale), null, null, null, null);
        }

        string verdict;
        if (string.Equals(record.Outcome, "failed", StringComparison.Ordinal))
        {
            verdict = Failing;
        }
        else if (string.Equals(record.Outcome, "succeeded", StringComparison.Ordinal)
                 && record.LastSuccessUtc is { } success
                 && nowUtc - success <= NightlyStaleAfter)
        {
            verdict = Ok;
        }
        else
        {
            // An old success, a success with no date, or an outcome nobody wrote: nothing proves a recent copy.
            verdict = Stale;
        }

        return new PlatformNightlyBackupDto(
            verdict,
            Label(verdict),
            record.Outcome,
            string.IsNullOrWhiteSpace(record.FailedStage) ? null : record.FailedStage,
            record.FinishedUtc,
            record.LastSuccessUtc);
    }

    public static PlatformWalArchiveDto EvaluateWal(WalArchiveRecord? record, DateTime nowUtc)
    {
        if (record is null)
        {
            return new PlatformWalArchiveDto(Stale, Label(Stale), null, 0, null);
        }

        string verdict;
        if (record.LastFailedUtc is { } failed
            && (record.LastArchivedUtc is not { } archivedBefore || failed > archivedBefore))
        {
            // The most recent push failed and none has succeeded since: PostgreSQL is retrying a segment and
            // `pg_wal` is growing on the server's disk.
            verdict = Failing;
        }
        else if (record.LastArchivedUtc is { } archived && nowUtc - archived <= WalStaleAfter)
        {
            verdict = Ok;
        }
        else
        {
            verdict = Stale;
        }

        return new PlatformWalArchiveDto(
            verdict, Label(verdict), record.LastArchivedUtc, record.FailedCount, record.LastFailedUtc);
    }

    public static string Label(string verdict) => verdict switch
    {
        Ok => "À jour",
        Stale => "En retard",
        Failing => "En échec",
        _ => verdict
    };

    private static string Worse(string a, string b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(string verdict) => verdict switch
    {
        Failing => 2,
        Stale => 1,
        _ => 0
    };
}
