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
    /// Generates a readable, cryptographically-random temporary password for an admin-driven
    /// reset. Always satisfies the minimum-length policy; the admin relays it to the user, who
    /// is forced to change it at next login.
    /// </summary>
    string GenerateTemporaryPassword();
}
