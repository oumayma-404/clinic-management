namespace ClinicManagement.API.Models;

/// <summary>
/// Body for <c>POST /api/backup</c> (US-8 / AC-8.1). An empty/omitted destination falls back to the
/// configured <c>Backup:DefaultDestination</c>.
/// </summary>
public class BackupRequest
{
    public string? DestinationFolder { get; set; }
}
