using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// Local (offline) authentication: PBKDF2 password hashing via <see cref="PasswordHasher{TUser}"/>
/// and issuance of app-signed JWTs. Registered only when <c>Auth:Mode = Local</c>.
/// </summary>
public class LocalAuthService : ILocalAuthService
{
    /// <summary>
    /// PBKDF2-HMAC-SHA512 with a 128-bit per-password CSPRNG salt (the framework's IdentityV3 format), at
    /// <b>210 000</b> iterations rather than the .NET 8 default of 100 000 — OWASP's current figure for
    /// PBKDF2-HMAC-SHA512.
    ///
    /// <para>⚠️ <b>The options must be passed in here, not registered in DI.</b> This field used to be
    /// <c>new()</c>, and <c>PasswordHasher&lt;T&gt;</c>'s parameterless constructor reads no configuration — so
    /// a <c>services.Configure&lt;PasswordHasherOptions&gt;(…)</c> would have looked correct, changed nothing,
    /// and left every password on the old work factor with a green build. That is this repository's « present
    /// and inert » shape.</para>
    ///
    /// <para><b>Existing accounts migrate on their own.</b> The stored format carries its own iteration count,
    /// so old hashes keep verifying; the verifier reports <c>SuccessRehashNeeded</c> and
    /// <c>LoginCommand</c>/<c>PlatformLoginCommand</c> already act on it, re-hashing at the new factor on the
    /// owner's next sign-in. Raising this number is therefore safe and needs no migration — which is exactly
    /// what that rehash path was built for.</para>
    /// </summary>
    private readonly PasswordHasher<User> _passwordHasher = new(
        Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));

    private readonly IConfiguration _configuration;

    public LocalAuthService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password is required.", nameof(password));
        }
        // PasswordHasher ignores the user argument for the default (v3/PBKDF2) format.
        return _passwordHasher.HashPassword(null!, password);
    }

    public PasswordVerificationOutcome VerifyPassword(string passwordHash, string providedPassword)
    {
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerificationOutcome.Failed;
        }

        var result = _passwordHasher.VerifyHashedPassword(null!, passwordHash, providedPassword);
        return result switch
        {
            PasswordVerificationResult.Success => PasswordVerificationOutcome.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerificationOutcome.SuccessNeedsRehash,
            _ => PasswordVerificationOutcome.Failed
        };
    }

    public LocalAuthToken GenerateToken(User user)
    {
        // The browser-held credential: short-lived on purpose, renewed silently from the cookie (AC-5.3).
        var expiresAt = DateTime.UtcNow.AddMinutes(LocalAuthConfig.AccessTokenLifetimeMinutes(_configuration));

        // Emit the same claim types ClinicContext + RoleAuthorizationHandler read in Cloud mode.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new("clinic_id", user.ClinicId.ToString()),
            new("role", user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // Compared against the account on every request so this token can be revoked despite the JWT
            // being stateless (US-5 / AC-5.1). A token WITHOUT this claim is rejected outright, which is what
            // retires the long-lived tokens issued before this shipped (AC-5.15).
            new(LocalAuthClaims.TokenVersion, user.TokenVersion.ToString(CultureInfo.InvariantCulture))
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim("email", user.Email));
        }

        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            claims.Add(new Claim("name", user.FullName));
        }

        var credentials = new SigningCredentials(
            LocalAuthConfig.SecurityKey(_configuration),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: LocalAuthConfig.Issuer(_configuration),
            audience: LocalAuthConfig.Audience(_configuration),
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return new LocalAuthToken(accessToken, expiresAt);
    }

    public LocalAuthToken GenerateRefreshToken(User user, Guid? sessionFamilyId)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(LocalAuthConfig.TokenLifetimeMinutes(_configuration));

        // The identity claims are included because the BFF decodes this token to render the header user
        // without a server round trip (AC-5.12). clinic_id is deliberately omitted: this token must never be
        // useful for anything but being exchanged.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new("role", user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(LocalAuthClaims.TokenVersion, user.TokenVersion.ToString(CultureInfo.InvariantCulture))
        };

        // FR-1.6 — which device's chain this credential belongs to. Only on the REFRESH token: an access token
        // is never exchanged, so it has no chain. Absent on a token minted before families existed, which the
        // exchange reads as « no chain to check » rather than as a replay.
        if (sessionFamilyId is { } familyId)
        {
            claims.Add(new Claim(LocalAuthClaims.SessionFamily, familyId.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim("email", user.Email));
        }

        if (!string.IsNullOrWhiteSpace(user.FullName))
        {
            claims.Add(new Claim("name", user.FullName));
        }

        var token = new JwtSecurityToken(
            issuer: LocalAuthConfig.Issuer(_configuration),
            // A DIFFERENT audience from the access token, which is what makes the API reject this outright
            // as a bearer token (AC-5.5).
            audience: LocalAuthConfig.RefreshAudience(_configuration),
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                LocalAuthConfig.SecurityKey(_configuration),
                SecurityAlgorithms.HmacSha256));

        return new LocalAuthToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public RefreshTokenPrincipal? ValidateRefreshToken(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = LocalAuthConfig.Issuer(_configuration),
            // Requiring the refresh audience is what stops an ACCESS token being replayed here to mint an
            // endless supply of new ones — the two token kinds are not interchangeable in either direction.
            ValidateAudience = true,
            ValidAudience = LocalAuthConfig.RefreshAudience(_configuration),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = LocalAuthConfig.SecurityKey(_configuration),
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            // JsonWebTokenHandler is what the runtime's JwtBearer uses, so validation here matches the API's
            // (the legacy JwtSecurityTokenHandler misreads its own `iss` on .NET 8 — see LEARNINGS). Fully
            // qualified rather than imported: the namespace also defines JwtRegisteredClaimNames, which would
            // become ambiguous with the System.IdentityModel.Tokens.Jwt one used throughout this file.
            var result = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler()
                .ValidateTokenAsync(refreshToken, parameters)
                .GetAwaiter().GetResult();

            if (!result.IsValid)
            {
                return null;
            }

            var subject = Claim(result, JwtRegisteredClaimNames.Sub);
            var version = Claim(result, LocalAuthClaims.TokenVersion);

            if (string.IsNullOrWhiteSpace(subject) ||
                !int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokenVersion))
            {
                // A refresh token with no usable version cannot be checked against the account, so it is not
                // trusted — the same rule that retires pre-upgrade tokens (AC-5.15).
                return null;
            }

            // ⚠️ An UNPARSEABLE or absent family is `null`, never a refusal: a credential minted before
            // families existed is an ordinary in-flight session, and refusing it would sign the whole
            // deployment out on the deploy that introduced replay detection.
            var family = Guid.TryParse(Claim(result, LocalAuthClaims.SessionFamily), out var familyId)
                ? familyId
                : (Guid?)null;

            return new RefreshTokenPrincipal(subject, tokenVersion, family);
        }
        catch
        {
            // Malformed / tampered / wrong key: indistinguishable from "not authenticated" to the caller.
            return null;
        }
    }

    private static string? Claim(Microsoft.IdentityModel.Tokens.TokenValidationResult result, string type) =>
        result.ClaimsIdentity?.FindFirst(type)?.Value;

    // An unambiguous alphabet (no 0/O/1/I/l) — an admin reads this aloud or writes it on paper.
    private const string TemporaryPasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    // ⚠️ Derived from the floor, never a literal that happens to match it: this was 12 while the floor was 8, so
    // raising the floor past 12 would have silently minted temporary passwords the five set-paths then refused —
    // an admin handing a colleague a credential the product will not accept, with nothing failing until they try.
    private static int TemporaryPasswordLength => PasswordPolicy.MinLength;

    public string GenerateTemporaryPassword()
    {
        var chars = new char[TemporaryPasswordLength];
        for (var i = 0; i < chars.Length; i++)
        {
            var index = RandomNumberGenerator.GetInt32(TemporaryPasswordAlphabet.Length);
            chars[i] = TemporaryPasswordAlphabet[index];
        }
        return new string(chars);
    }
}
