using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The bearer FCM's HTTP v1 API accepts: an OAuth access token minted from the Firebase service-account key.
///
/// <para>⚠️ <b>The key itself is not a bearer.</b> <see cref="FcmPushSender"/> used to send
/// <c>Push:Fcm:ServiceAccountKey</c> verbatim as <c>Authorization: Bearer …</c>, which Google refuses with 401 — so
/// the sender could never deliver with the very value its configuration asked for. The key is exchanged here (a
/// signed JWT, RFC 7523) for a one-hour token, cached until shortly before it expires.</para>
///
/// <para>A secret that is not JSON is passed through as the bearer, which keeps an operator who configured a
/// short-lived access token by hand working exactly as before.</para>
/// </summary>
public static class FcmAccessToken
{
    public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private const string DefaultTokenUri = "https://oauth2.googleapis.com/token";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, CachedToken> Cache = new();

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);

    /// <summary>What resolving produced: a bearer, or why there is none.</summary>
    public sealed record Outcome(string? Bearer, bool Misconfigured, string? Error)
    {
        public static Outcome Of(string bearer) => new(bearer, false, null);
        public static Outcome BadKey(string error) => new(null, true, error);
        public static Outcome Failed(string error) => new(null, false, error);
    }

    public static async Task<Outcome> ResolveAsync(
        HttpClient client, string secret, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var trimmed = secret.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return Outcome.Of(trimmed);
        }

        ServiceAccount account;
        try
        {
            account = ServiceAccount.Parse(trimmed);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or CryptographicException or KeyNotFoundException)
        {
            return Outcome.BadKey($"Clé de compte de service Firebase illisible : {ex.Message}");
        }

        var cacheKey = account.CacheKey;
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt - RefreshMargin > now)
        {
            return Outcome.Of(cached.Token);
        }

        string assertion;
        try
        {
            assertion = account.SignedAssertion(now);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            // ImportFromPem throws ArgumentException for text that holds no PEM block at all.
            return Outcome.BadKey($"Clé privée Firebase inutilisable : {ex.Message}");
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = assertion
        });
        using var response = await client.PostAsync(account.TokenUri, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var reason = $"Google a refusé le jeton Firebase ({(int)response.StatusCode}) : {Truncate(body)}";
            // 400/401 here is the key itself (revoked, wrong project): no retry changes it.
            return (int)response.StatusCode is 400 or 401 ? Outcome.BadKey(reason) : Outcome.Failed(reason);
        }

        using var json = JsonDocument.Parse(body);
        var token = json.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Outcome.Failed("Google n'a renvoyé aucun jeton Firebase.");
        }

        var lifetime = json.RootElement.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromHours(1);
        Cache[cacheKey] = new CachedToken(token, now + lifetime);
        return Outcome.Of(token);
    }

    /// <summary>Test seam: forgets every cached token.</summary>
    public static void ClearCache() => Cache.Clear();

    private static string Truncate(string body) => body.Length > 200 ? body[..200] : body;

    private sealed record ServiceAccount(string ClientEmail, string PrivateKeyPem, string TokenUri)
    {
        public string CacheKey => ClientEmail + "|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PrivateKeyPem)));

        public static ServiceAccount Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var email = root.GetProperty("client_email").GetString();
            var key = root.GetProperty("private_key").GetString();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("client_email ou private_key manquant.");
            }

            var tokenUri = root.TryGetProperty("token_uri", out var uri) && !string.IsNullOrWhiteSpace(uri.GetString())
                ? uri.GetString()!
                : DefaultTokenUri;
            return new ServiceAccount(email, key, tokenUri);
        }

        public string SignedAssertion(DateTimeOffset now)
        {
            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = ClientEmail,
                scope = Scope,
                aud = TokenUri,
                iat = now.ToUnixTimeSeconds(),
                exp = now.AddHours(1).ToUnixTimeSeconds()
            }));
            var signingInput = $"{header}.{claims}";
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PrivateKeyPem);
            var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return $"{signingInput}.{Base64Url(signature)}";
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
