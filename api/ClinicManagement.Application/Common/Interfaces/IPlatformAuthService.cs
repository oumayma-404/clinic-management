using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>A console session token plus its expiry — the body of the spec's 200 on sign-in.</summary>
public record PlatformAuthToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// The console's own credential primitives: password hashing and <b>token issuance under its own signing key,
/// issuer and audience</b> (FR-1, AC-1.4).
///
/// <para><b>The distinct issuer and audience are the mechanism, not decoration.</b> AC-1.4 asks that a console
/// token on a clinic route and a clinic token on a console route are both refused as <i>unauthenticated</i>
/// rather than merely unauthorised — and that is what each scheme failing the other's issuer/audience validation
/// produces, by construction, with no policy involved. A shared key with different claims would make the refusal
/// an authorization decision instead, i.e. a 403 and one forgotten attribute away from working.</para>
///
/// <para><b>Password hashing is delegated to <see cref="ILocalAuthService"/>'s PBKDF2 rather than re-implemented</b>
/// — one hasher for both populations means a future parameter bump reaches both, and the clinic side's
/// rehash-on-login outcome is reused verbatim.</para>
/// </summary>
public interface IPlatformAuthService
{
    string HashPassword(string password);

    PasswordVerificationOutcome VerifyPassword(string passwordHash, string providedPassword);

    /// <summary>
    /// Issues the console session token: <c>sub</c> (the account id), <c>email</c>, <c>token_version</c> and
    /// <see cref="IPlatformSessionContext.TokenKindClaim"/>. It carries <b>no clinic and no role</b> — a console
    /// account has neither, and emitting an empty one would let a clinic-side policy resolve against it.
    /// </summary>
    PlatformAuthToken GenerateToken(PlatformAccount account);

    /// <summary>A readable CSPRNG password for the bootstrap verb to print. Same alphabet policy as the clinic side.</summary>
    string GenerateTemporaryPassword();
}
