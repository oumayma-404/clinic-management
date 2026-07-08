namespace ClinicManagement.Application.DTOs;

/// <summary>
/// Result of an admin password reset (AC-5.2). The temporary password is returned exactly
/// once for the admin to relay to the user; it is never stored in plain text.
/// </summary>
public class ResetPasswordResultDto
{
    public string UserId { get; set; } = string.Empty;
    public string TemporaryPassword { get; set; } = string.Empty;
}
