namespace ClinicManagement.API.Models;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The one-time code, where the account holds a second factor
    /// (<c>hosted-security-hardening</c> FR-1.2). Absent on the first request of a two-step sign-in.
    /// </summary>
    public string? TotpCode { get; set; }
}

/// <summary>Step one carries no code; step two carries the one generated from the issued secret.</summary>
public class EnrolTotpRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? TotpCode { get; set; }
}

/// <summary>A sign-in that presents a single-use recovery code instead of the authenticator.</summary>
public class RedeemRecoveryCodeRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RecoveryCode { get; set; } = string.Empty;
}
