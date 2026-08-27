using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// One-click "Backup now" mechanism (US-8 / FR-G). Produces a consistent snapshot of the
/// PostgreSQL database plus the file-storage folder into a timestamped destination folder.
/// A seam so the <c>pg_dump</c> shell-out stays behind an interface — the command handler is
/// unit-testable and the concrete implementation (<c>PgDumpBackupService</c>) is mockable.
/// </summary>
/// <remarks>
/// Implementations MUST surface operator-facing failures as distinct, non-silent errors
/// (destination unwritable, disk full, <c>pg_dump</c> not found, dump failed) — AC-8.2 / AC-8.3.
/// </remarks>
public interface IBackupService
{
    /// <summary>
    /// Writes a DB dump + a recursive copy of the file-storage folder under a
    /// <c>clinic-backup-&lt;yyyyMMdd-HHmmss&gt;</c> subfolder of <paramref name="destinationFolder"/>
    /// (or the configured default destination when null/empty).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown with a clear operator-facing message when the backup cannot be completed
    /// (unwritable destination, insufficient disk space, missing <c>pg_dump</c>, a failed dump, or — since
    /// L4c — a dump that <c>pg_restore --list</c> cannot read).
    /// </exception>
    Task<BackupResultDto> CreateBackupAsync(string? destinationFolder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Where a backup would be written, given a caller-supplied folder or nothing (L4b).
    ///
    /// <para>Exposed rather than kept private because two other things have to name the same path: the settings
    /// panel, which promises « Laissez le champ vide pour utiliser le dossier par défaut du serveur » and could
    /// not say which folder that is, and the restore verb's printed command line. A second resolution rule in
    /// either place is a printed path that does not match where the file actually went.</para>
    ///
    /// <para>Never throws and never returns empty: the last fallback is install-relative.</para>
    /// </summary>
    string ResolveDestinationRoot(string? destinationFolder);

    /// <summary>
    /// Deletes the oldest backup folders beyond <paramref name="keepCount"/> and returns how many went (L4a).
    ///
    /// <para>Three guarantees, all of them the difference between retention and data loss: it matches
    /// <b>only</b> folders named <c>clinic-backup-*</c> (an operator's own folder in the same destination is not
    /// the pruner's business), it deletes <b>oldest first</b>, and it <b>never deletes the last surviving
    /// backup</b> whatever the count says — an empty backup folder is the one state retention must not be able
    /// to produce.</para>
    ///
    /// <para>Best-effort: a folder it cannot delete is logged and skipped, because failing here must not fail the
    /// backup that just succeeded.</para>
    /// </summary>
    Task<int> PruneOldBackupsAsync(
        string? destinationFolder, int keepCount, CancellationToken cancellationToken = default);
}
