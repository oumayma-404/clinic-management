namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// What the host's two off-site copies last did (<c>server-loss-recovery</c> Part 3) — the nightly run's status file
/// and PostgreSQL's own WAL archiver — read as facts, judged by <c>BackupHealthRules</c>.
///
/// <para><b>Why the application reads them at all.</b> Both copies are made by sidecars the application never
/// calls, and on 2026-10-06 the nightly one had failed for 33 nights with its only record a <c>docker logs</c> line
/// nobody read. A copy that fails must reach a person; the API is what can email one and draw a console screen.</para>
///
/// <para>⚠️ <b>Never throws.</b> A file that is missing, unreadable or half-written, and a database that refuses
/// the statistics read, are each answered as an absent record — which the rules grade as « not proven », never as
/// « fine ». An exception here would turn the one screen that says the backup is broken into an error page.</para>
/// </summary>
public interface IHostBackupStatusReader
{
    Task<HostBackupSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

/// <param name="Nightly">The nightly run's last recorded outcome; null when no run has been recorded.</param>
/// <param name="WalArchive">PostgreSQL's archiver statistics; null when they could not be read.</param>
public sealed record HostBackupSnapshot(NightlyBackupRecord? Nightly, WalArchiveRecord? WalArchive);

/// <summary>One line of <c>backup.status</c> each — the file <c>deploy/backup/backup.sh</c> writes on every run.</summary>
/// <param name="Outcome"><c>succeeded</c> or <c>failed</c>.</param>
/// <param name="FailedStage">The step a failed run stopped at (<c>remote</c>, <c>dump</c>, <c>upload</c>…); null on success.</param>
/// <param name="FinishedUtc">When the run ended.</param>
/// <param name="LastSuccessUtc">When a run last reached the off-site bucket — carried over by a failed run.</param>
public sealed record NightlyBackupRecord(
    string Outcome,
    string? FailedStage,
    DateTime? FinishedUtc,
    DateTime? LastSuccessUtc);

/// <summary>The four columns of <c>pg_stat_archiver</c> that say whether WAL is reaching the bucket.</summary>
public sealed record WalArchiveRecord(
    long ArchivedCount,
    DateTime? LastArchivedUtc,
    long FailedCount,
    DateTime? LastFailedUtc);
