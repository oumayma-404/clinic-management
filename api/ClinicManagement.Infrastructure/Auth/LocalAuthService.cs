using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// Local (offline) authentication: PBKDF2 password hashing via <see cref="PasswordHasher{TUser}"/>
/// and issuance of app-signed JWTs. Registered only when <c>Auth:Mode = Local</c>.
/// </summary>
public class LocalAuthService : ILocalAuthService
{
    private readonly PasswordHasher<User> _passwordHasher = new();
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
        var expiresAt = DateTime.UtcNow.AddMinutes(LocalAuthConfig.TokenLifetimeMinutes(_configuration));

        // Emit the same claim types ClinicContext + RoleAuthorizationHandler read in Cloud mode.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new("clinic_id", user.ClinicId.ToString()),
            new("role", user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
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

    // 12 chars from an unambiguous alphabet (no 0/O/1/I/l) — comfortably above the 8-char
    // minimum and easy for an admin to read aloud to the user.
    private const string TemporaryPasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
    private const int TemporaryPasswordLength = 12;

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
