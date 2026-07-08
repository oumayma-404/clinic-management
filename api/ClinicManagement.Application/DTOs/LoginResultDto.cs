namespace ClinicManagement.Application.DTOs;

/// <summary>Result of a successful Local-mode login.</summary>
public class LoginResultDto
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool MustChangePassword { get; set; }
    public UserDto User { get; set; } = null!;
}
