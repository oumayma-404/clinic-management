namespace ClinicManagement.Application.DTOs;

/// <summary>Result of a successful Local-mode login.</summary>
public class LoginResultDto
{
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The durable session credential the BFF stores in its HttpOnly cookie (security-hardening US-5). The API
    /// rejects this as a bearer token — it can only be exchanged. A refresh **also** returns a fresh one, which
    /// is what makes the session slide rather than die 12 h after sign-in (mobile-native-shells AC-35).
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>When the <see cref="AccessToken"/> expires — minutes, not hours (AC-5.3).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the <see cref="RefreshToken"/> expires — the durable session's real lifetime, hours not minutes.
    /// The BFF needs this separately to set its cookie: keying the cookie off <see cref="ExpiresAt"/> made the
    /// browser discard a still-valid 12h session after the 30-minute access token lapsed. Null only where no
    /// refresh token was issued at all; both the login and the refresh paths populate it.
    /// </summary>
    public DateTime? RefreshExpiresAt { get; set; }
    public bool MustChangePassword { get; set; }
    public UserDto User { get; set; } = null!;
}
