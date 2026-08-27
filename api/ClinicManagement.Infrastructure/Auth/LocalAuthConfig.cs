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
    private const int DefaultTokenLifetimeMinutes = 720; // 12h durable session; frontend enforces inactivity expiry (AC-3.5)
    private const int DefaultAccessTokenLifetimeMinutes = 30; // browser-held credential, renewed silently (AC-5.3)

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

    /// <summary>
    /// Lifetime of the <b>durable session</b> — the refresh token held in the HttpOnly cookie. This is how
    /// long a staff member stays signed in without re-entering their password, and it keeps the value the
    /// single access token used to have, so the felt session length is unchanged.
    /// </summary>
    public static int TokenLifetimeMinutes(IConfiguration configuration) =>
        configuration.GetValue<int?>("Auth:Local:TokenLifetimeMinutes") ?? DefaultTokenLifetimeMinutes;

    /// <summary>
    /// Lifetime of the <b>access token</b> the browser actually holds (security-hardening AC-5.3). Short on
    /// purpose: this is the credential exposed to browser JavaScript, so a stolen one must die quickly. The
    /// user never notices, because the client renews silently from the cookie (AC-5.4).
    /// </summary>
    public static int AccessTokenLifetimeMinutes(IConfiguration configuration) =>
        configuration.GetValue<int?>("Auth:Local:AccessTokenLifetimeMinutes") ?? DefaultAccessTokenLifetimeMinutes;

    /// <summary>
    /// Audience of the refresh token. Deliberately <b>different</b> from <see cref="Audience"/>, because the
    /// API's bearer validation requires that audience — so the cookie's credential is rejected outright as an
    /// API token (AC-5.5). Stealing it therefore buys nothing that can call the API directly; it can only be
    /// exchanged, and the exchange re-checks live account state.
    /// </summary>
    public static string RefreshAudience(IConfiguration configuration) =>
        configuration["Auth:Local:RefreshAudience"] ?? $"{Audience(configuration)}-refresh";

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

        // Resolve the per-install key file against the install directory (R-6) so it is found the same
        // way whether launched from a console or as a Windows service (whose CWD is System32).
        var path = configuration["Auth:Local:SigningKeyPath"]
                   ?? LocalInstallPaths.LocalFile("signing-key");

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
