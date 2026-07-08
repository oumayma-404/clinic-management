using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// Shared reader for the <c>Auth:*</c> configuration used by Local (offline) mode.
/// Both the token issuer (<c>LocalAuthService</c>) and the token validator (<c>Program.cs</c>)
/// resolve issuer/audience/lifetime and — crucially — the same per-install signing key here.
/// </summary>
public static class LocalAuthConfig
{
    public const string LocalMode = "Local";
    public const string CloudMode = "Cloud";

    private const string DefaultIssuer = "clinic-management-local";
    private const string DefaultAudience = "clinic-management-local-api";
    private const int DefaultTokenLifetimeMinutes = 720; // 12h; frontend enforces inactivity expiry (AC-3.5)

    private static readonly object KeyFileLock = new();

    // The signing key is per-install and immutable at runtime; resolve it once (it is read on
    // every token issuance) instead of hitting the disk each time.
    private static byte[]? _cachedSigningKey;

    /// <summary>True when the server is configured for Local (offline email+password) auth.</summary>
    public static bool IsLocalMode(IConfiguration configuration) =>
        string.Equals(configuration["Auth:Mode"], LocalMode, StringComparison.OrdinalIgnoreCase);

    public static string Issuer(IConfiguration configuration) =>
        configuration["Auth:Local:Issuer"] ?? DefaultIssuer;

    public static string Audience(IConfiguration configuration) =>
        configuration["Auth:Local:Audience"] ?? DefaultAudience;

    public static int TokenLifetimeMinutes(IConfiguration configuration) =>
        configuration.GetValue<int?>("Auth:Local:TokenLifetimeMinutes") ?? DefaultTokenLifetimeMinutes;

    public static SymmetricSecurityKey SecurityKey(IConfiguration configuration) =>
        new(ResolveSigningKey(configuration));

    /// <summary>
    /// Resolves the per-install signing key. Priority:
    /// 1. explicit <c>Auth:Local:SigningKey</c> (base64 or raw; must be ≥ 256 bits),
    /// 2. a key file at <c>Auth:Local:SigningKeyPath</c> (default <c>.local/signing-key</c>),
    ///    generated on first run and reused thereafter.
    /// The key is never committed and never written to appsettings.
    /// </summary>
    public static byte[] ResolveSigningKey(IConfiguration configuration)
    {
        if (_cachedSigningKey is not null)
        {
            return _cachedSigningKey;
        }

        lock (KeyFileLock)
        {
            _cachedSigningKey ??= LoadSigningKey(configuration);
            return _cachedSigningKey;
        }
    }

    private static byte[] LoadSigningKey(IConfiguration configuration)
    {
        var configured = configuration["Auth:Local:SigningKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(configured);
            }
            catch (FormatException)
            {
                bytes = Encoding.UTF8.GetBytes(configured);
            }

            if (bytes.Length < 32)
            {
                throw new InvalidOperationException(
                    "Auth:Local:SigningKey must be at least 32 bytes (256 bits).");
            }
            return bytes;
        }

        var path = configuration["Auth:Local:SigningKeyPath"]
                   ?? Path.Combine(Directory.GetCurrentDirectory(), ".local", "signing-key");

        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                try
                {
                    return Convert.FromBase64String(existing);
                }
                catch (FormatException)
                {
                    throw new InvalidOperationException(
                        $"The local signing key file at '{path}' is corrupted (not valid base64). " +
                        "Delete it to regenerate a new key, or restore it from a backup.");
                }
            }
        }

        var key = RandomNumberGenerator.GetBytes(64);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, Convert.ToBase64String(key));
        return key;
    }
}
