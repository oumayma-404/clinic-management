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
    /// (unwritable destination, insufficient disk space, missing <c>pg_dump</c>, or a failed dump).
    /// </exception>
    Task<BackupResultDto> CreateBackupAsync(string? destinationFolder, CancellationToken cancellationToken = default);
}
