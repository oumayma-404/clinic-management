namespace ClinicManagement.Application.DTOs;

/// <summary>
/// Result of a successful one-click backup (US-8 / FR-G / AC-8.1, AC-8.2). Reports the exact
/// destination folder so the admin knows where the DB dump + file copy landed, plus the total
/// size on disk and the (UTC) moment it completed.
/// </summary>
public class BackupResultDto
{
    /// <summary>Absolute path of the timestamped backup folder that was written.</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Total size of the backup (DB dump + copied files) in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>When the backup completed (UTC — dates are UTC everywhere in this codebase).</summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// How many objects <c>pg_restore --list</c> found in the dump (L4c).
    ///
    /// <para><c>pg_dump</c> exiting 0 is not proof that anything is restorable, and the service previously only
    /// measured the folder's size — a truncated or zero-byte dump has a size too. Reading the table of contents
    /// back is fast, read-only and needs no target database. The <b>count</b> is reported rather than a boolean
    /// because « 3 objets » where the schema has thirty-eight tables is a detectable disaster that a green tick
    /// cannot express.</para>
    /// </summary>
    public int VerifiedObjectCount { get; set; }

    /// <summary>
    /// Set when the backup succeeded but could <b>not</b> be access-restricted — a removable or network
    /// destination, where NTFS permissions cannot be relied on (US-14 / AC-14.3). The backup is valid; the
    /// copy of the patient records in it is simply readable by anyone who can reach that medium, so the admin
    /// must be told rather than only the log. <c>null</c> when the destination was locked down normally.
    /// </summary>
    public string? Warning { get; set; }
}
