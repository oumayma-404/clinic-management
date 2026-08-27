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

/// <summary>A current one-time code, proving the caller holds the authenticator right now.</summary>
public class TotpCodeRequest
{
    public string TotpCode { get; set; } = string.Empty;
}

/// <summary>
/// A step-up: the action to authorise, and either proof of presence.
///
/// <para>Both proofs are optional and exactly one is needed — a shell user who signs in by biometrics may not
/// remember their password, so demanding it would make the guarded action unreachable for them (OQ-2).</para>
/// </summary>
public class StepUpRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? TotpCode { get; set; }
}

/// <summary>Carries the single-use token a step-up minted, for an action that demands one.</summary>
public class StepUpConfirmationRequest
{
    public string? ConfirmationToken { get; set; }
}
