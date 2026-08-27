using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Outcome of verifying a supplied password against a stored hash.
/// </summary>
public enum PasswordVerificationOutcome
{
    Failed,
    Success,
    /// <summary>Password is correct but the stored hash uses an outdated format and should be re-hashed.</summary>
    SuccessNeedsRehash
}

/// <summary>A locally-issued session token plus its expiry (Local mode only).</summary>
public record LocalAuthToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// What a validated refresh token asserts. The version must still be checked against the account before a
/// new access token is issued — the signature only proves the token was ours, not that the session is live.
/// </summary>
/// <param name="SessionFamilyId">
/// The <c>SessionFamily</c> this credential belongs to (<c>hosted-security-hardening</c> FR-1.6), or
/// <c>null</c> on a token minted before families existed — which is treated as « no chain to check » rather
/// than as a replay, so an in-flight session survives the deploy.
/// </param>
public record RefreshTokenPrincipal(string Subject, int TokenVersion, Guid? SessionFamilyId);

/// <summary>
/// Local (offline) authentication primitives: password hashing/verification and
/// issuance of app-signed JWTs. Only used when <c>Auth:Mode = Local</c>.
/// </summary>
public interface ILocalAuthService
{
    string HashPassword(string password);

    PasswordVerificationOutcome VerifyPassword(string passwordHash, string providedPassword);

    /// <summary>
    /// Issues a signed JWT carrying the <c>sub</c>, <c>email</c>, <c>role</c> and
    /// <c>clinic_id</c> claims that <see cref="IClinicContext"/> and the role handlers expect.
    /// </summary>
    LocalAuthToken GenerateToken(User user);

    /// <summary>
    /// Issues the <b>durable session</b> credential stored in the HttpOnly cookie (security-hardening US-5).
    ///
    /// <para>Carries a different audience from <see cref="GenerateToken"/>, so the API's bearer validation
    /// rejects it outright — stealing the cookie buys nothing that can call the API (AC-5.5). It can only be
    /// exchanged via <see cref="ValidateRefreshToken"/>, and that exchange re-checks live account state.</para>
    ///
    /// <para>It still carries <c>email</c>, <c>name</c> and <c>role</c>, because the BFF decodes this token to
    /// render the header identity without a server round trip (AC-5.12).</para>
    /// </summary>
    /// <param name="sessionFamilyId">
    /// The chain this credential belongs to (FR-1.6). Stamped into the token so a replayed credential can be
    /// traced back to its device even when it is too old to match either stored hash.
    /// </param>
    LocalAuthToken GenerateRefreshToken(User user, Guid? sessionFamilyId);

    /// <summary>
    /// Validates a refresh token's signature, issuer, refresh audience and lifetime, returning the subject and
    /// token version it asserts — or <c>null</c> if it is invalid or is an access token being misused here.
    /// The caller must still confirm the version against the account, so a revoked session cannot renew
    /// (AC-5.6).
    /// </summary>
    RefreshTokenPrincipal? ValidateRefreshToken(string refreshToken);

    /// <summary>
    /// Generates a readable, cryptographically-random temporary password for an admin-driven
    /// reset. Always satisfies the minimum-length policy; the admin relays it to the user, who
    /// is forced to change it at next login.
    /// </summary>
    string GenerateTemporaryPassword();
}
