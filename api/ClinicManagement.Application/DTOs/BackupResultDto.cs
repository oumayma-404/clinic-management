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
}
