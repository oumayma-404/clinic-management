namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// Claim names the locally-issued JWT carries beyond the registered ones. Shared by the issuer
/// (<see cref="LocalAuthService"/>) and the per-request validator (the API's
/// <c>LocalAuthEnforcementMiddleware</c>) so the two can never drift on a spelling — the same reason
/// <see cref="LocalAuthConfig"/> owns the signing key for both sides.
/// </summary>
public static class LocalAuthClaims
{
    /// <summary>
    /// The account's token version. Present on every token issued from this release onward; its <b>absence</b>
    /// is what marks a token as pre-upgrade and therefore invalid (security-hardening AC-5.15).
    /// </summary>
    public const string TokenVersion = "token_version";
}
