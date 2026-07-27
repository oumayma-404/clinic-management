namespace ClinicManagement.Application.DTOs;

/// <summary>Result of a successful Local-mode login.</summary>
public class LoginResultDto
{
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The durable session credential the BFF stores in its HttpOnly cookie (security-hardening US-5). Empty
    /// on a refresh, where only a new access token is issued. The API rejects this as a bearer token — it can
    /// only be exchanged.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>When the <see cref="AccessToken"/> expires — minutes, not hours (AC-5.3).</summary>
    public DateTime ExpiresAt { get; set; }
    public bool MustChangePassword { get; set; }
    public UserDto User { get; set; } = null!;
}
