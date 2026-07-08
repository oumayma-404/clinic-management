namespace ClinicManagement.API.Models;

/// <summary>First-run setup payload (Local mode): creates the clinic + first admin.</summary>
public class SetupRequest
{
    public string ClinicName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
}
