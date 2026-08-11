using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// The console's credential primitives: PBKDF2 hashing <b>through the clinic side's own hasher</b> and HS256
/// token issuance under <see cref="PlatformAuthConfig"/>'s separate key, issuer and audience.
///
/// <para><b>Hashing is delegated, issuance is not.</b> One hasher for both populations means a future parameter
/// bump reaches both and the rehash-on-login outcome is the same object; one <i>signing key</i> for both would
/// destroy the isolation this class exists for. That asymmetry is the whole design in a sentence.</para>
///
/// <para>⚠️ <b>The token carries no <c>clinic_id</c> and no <c>role</c>.</b> A console account has neither, and
/// emitting an empty value would give <c>RoleAuthorizationHandler</c> and <c>TenantScopeMiddleware</c> something
/// to resolve against — a clinic-side gate would then be evaluating a principal it was never meant to see.</para>
/// </summary>
public class PlatformAuthService : IPlatformAuthService
{
    private readonly ILocalAuthService _passwordHashing;
    private readonly IConfiguration _configuration;

    public PlatformAuthService(ILocalAuthService passwordHashing, IConfiguration configuration)
    {
        _passwordHashing = passwordHashing;
        _configuration = configuration;
    }

    public string HashPassword(string password) => _passwordHashing.HashPassword(password);

    public PasswordVerificationOutcome VerifyPassword(string passwordHash, string providedPassword) =>
        _passwordHashing.VerifyPassword(passwordHash, providedPassword);

    public string GenerateTemporaryPassword() => _passwordHashing.GenerateTemporaryPassword();

    public PlatformAuthToken GenerateToken(PlatformAccount account)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(PlatformAuthConfig.TokenLifetimeMinutes(_configuration));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
            new("email", account.Email),
            new("name", account.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // Compared on every console request, so a deactivation or a password change kills this token on the
            // NEXT call rather than at expiry (AC-1.6). Same mechanism as the clinic side's token_version.
            new(LocalAuthClaims.TokenVersion, account.TokenVersion.ToString(CultureInfo.InvariantCulture)),
            // What makes a console principal recognisable to IPlatformSessionContext, and therefore what makes
            // the audit actor `console|…` rather than a bare GUID. See that interface for why not the sub's shape.
            new(IPlatformSessionContext.TokenKindClaim, IPlatformSessionContext.PlatformTokenKind)
        };

        var token = new JwtSecurityToken(
            issuer: PlatformAuthConfig.Issuer(_configuration),
            audience: PlatformAuthConfig.Audience(_configuration),
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                PlatformAuthConfig.SecurityKey(_configuration),
                SecurityAlgorithms.HmacSha256));

        return new PlatformAuthToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
