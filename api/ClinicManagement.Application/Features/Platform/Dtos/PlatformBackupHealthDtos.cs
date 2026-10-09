namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// Whether the host's two off-site copies are healthy (<c>server-loss-recovery</c> Part 3) — the vendor console's
/// status strip and the daily alert e-mail both read this one shape.
///
/// <para>⚠️ <b>Facts about the deployment, never about a cabinet.</b> Every leaf is an outcome, a step name, an
/// instant or a count of WAL segments; none can name a practice, a patient or an amount, which is what admits them
/// to <c>PlatformReadShape</c>.</para>
/// </summary>
/// <param name="Verdict">The worse of the two copies' verdicts: <c>Ok</c>, <c>Stale</c> or <c>Failing</c>.</param>
/// <param name="VerdictLabel">The French wording of <paramref name="Verdict"/>.</param>
/// <param name="CheckedAt">The instant the verdicts were computed against.</param>
public sealed record PlatformBackupHealthDto(
    string Verdict,
    string VerdictLabel,
    DateTime CheckedAt,
    PlatformNightlyBackupDto Nightly,
    PlatformWalArchiveDto WalArchive);

/// <summary>The nightly run: the database dump, every stored file and the key ring.</summary>
/// <param name="Outcome"><c>succeeded</c> / <c>failed</c> as the run wrote it; null when no run has been recorded.</param>
/// <param name="FailedStage">The step a failed run stopped at; null on success.</param>
/// <param name="LastRunAt">When the last run — successful or not — ended.</param>
/// <param name="LastSuccessAt">When a run last reached the off-site bucket.</param>
public sealed record PlatformNightlyBackupDto(
    string Verdict,
    string VerdictLabel,
    string? Outcome,
    string? FailedStage,
    DateTime? LastRunAt,
    DateTime? LastSuccessAt);

/// <summary>The continuous copy of the database (WAL-G), read from PostgreSQL's own archiver statistics.</summary>
/// <param name="LastArchivedAt">When the last WAL segment reached the bucket.</param>
/// <param name="FailedCount">Failed pushes since the statistics were last reset — a total, not a streak.</param>
/// <param name="LastFailedAt">When a push last failed.</param>
public sealed record PlatformWalArchiveDto(
    string Verdict,
    string VerdictLabel,
    DateTime? LastArchivedAt,
    long FailedCount,
    DateTime? LastFailedAt);
