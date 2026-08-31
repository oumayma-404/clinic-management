using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// The console's own <c>Console:*</c> settings — read by <b>both</b> the token issuer
/// (<see cref="PlatformAuthService"/>) and the token validator (<c>Program.cs</c>), so the two cannot drift, the
/// same arrangement <see cref="LocalAuthConfig"/> has for the clinic side.
///
/// <para>⚠️ <b>Its signing key is never the clinic's, and there is no fallback to it.</b> An absent
/// <c>Console:SigningKey</c> <b>throws</b> where the console is bound rather than borrowing
/// <c>Auth:Local:SigningKey</c> — a shared key would make a clinic token and a console token differ only by their
/// claims, and AC-1.4's « refused as unauthenticated » would become an authorization decision one forgotten
/// attribute away from working. It is also why there is no generated-key-file path here as
/// <see cref="LocalAuthConfig"/> has: this profile is a container fleet, so a per-instance generated key would
/// mean a session minted by one replica is rejected by the next.</para>
///
/// <para>The issuer and audience are distinct from the clinic's <i>by default</i>, not merely by convention — see
/// <see cref="DefaultIssuer"/>. That is what makes each scheme fail the other's validation by construction.</para>
/// </summary>
public static class PlatformAuthConfig
{
    public const string PortKey = "Console:Port";
    public const string SigningKeyKey = "Console:SigningKey";

    /// <summary>Distinct from <c>clinic-management-local</c>, which is the whole mechanism behind AC-1.4.</summary>
    private const string DefaultIssuer = "clinic-management-console";

    private const string DefaultAudience = "clinic-management-console-api";

    /// <summary>
    /// Four hours. Long enough that a vendor working through a portfolio is not re-authenticating with a
    /// one-time code every hour, short enough that a forgotten session on a laptop dies the same day — and the
    /// live-state check on every request (<c>PlatformAccountStateMiddleware</c>) is what covers the interval.
    /// </summary>
    private const int DefaultTokenLifetimeMinutes = 240;

    /// <summary>The port the console listener binds, or <c>0</c> when the console is switched off.</summary>
    public static int Port(IConfiguration configuration) =>
        configuration.GetValue<int?>(PortKey) ?? 0;

    public static string Issuer(IConfiguration configuration) =>
        configuration["Console:Issuer"] ?? DefaultIssuer;

    public static string Audience(IConfiguration configuration) =>
        configuration["Console:Audience"] ?? DefaultAudience;

    public static int TokenLifetimeMinutes(IConfiguration configuration) =>
        configuration.GetValue<int?>("Console:TokenLifetimeMinutes") ?? DefaultTokenLifetimeMinutes;

    public static SymmetricSecurityKey SecurityKey(IConfiguration configuration) =>
        new(ResolveSigningKey(configuration));

    /// <summary>
    /// The configured key as bytes — base64 if it parses as such, otherwise its UTF-8 bytes, exactly as
    /// <see cref="LocalAuthConfig"/> reads its own. Throws on absent or too-short: where the console is bound,
    /// starting without a key would mean issuing sessions signed with nothing anyone chose.
    /// </summary>
    public static byte[] ResolveSigningKey(IConfiguration configuration)
    {
        var configured = configuration[SigningKeyKey];

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{SigningKeyKey} est obligatoire lorsque la console éditeur est activée ({PortKey} > 0). "
                + "Fournissez une clé d'au moins 32 octets via l'environnement (Console__SigningKey). "
                + "Elle ne doit jamais être la clé de signature des cliniques (Auth:Local:SigningKey).");
        }

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
            throw new InvalidOperationException($"{SigningKeyKey} doit faire au moins 32 octets (256 bits).");
        }

        // ⚠️ A placeholder is not a key. `MinioCredentials` already refuses the published `minioadmin` for this
        // reason — « a credential that is only decorative is treated as absent » — and the console key had no
        // such check, so a `.env` copied from the example and never filled in produced a deployment that starts,
        // reports healthy, and signs the vendor's own sessions with a value published in this repository.
        if (LooksLikeAPlaceholder(configured))
        {
            throw new InvalidOperationException(
                $"{SigningKeyKey} porte encore une valeur d'exemple. Générez une vraie clé "
                + "(`openssl rand -base64 48`) et remplacez-la ; une clé publiée dans ce dépôt n'en est pas une.");
        }

        // ⚠️ THE check this class's own error message has been promising since it was written: « Elle ne doit
        // jamais être la clé de signature des cliniques ». Nothing enforced it. Sharing one key across the two
        // issuers collapses the vendor/tenant boundary — the audiences differ, but a single leaked key then
        // mints BOTH a clinic session and a console session, and the console can read every cabinet's portfolio.
        // An operator setting both from one generated secret is the obvious, tidy-looking mistake.
        if (SharesTheClinicSigningKey(configuration, bytes))
        {
            throw new InvalidOperationException(
                $"{SigningKeyKey} est identique à Auth:Local:SigningKey. La console éditeur et les cabinets "
                + "doivent avoir des clés distinctes : une seule clé compromise ouvrirait les deux.");
        }

        return bytes;
    }

    /// <summary>
    /// Placeholder values shipped in this repository's own examples. Matched case-insensitively on a prefix,
    /// because the examples suffix a hint (<c>CHANGE_ME_strong_db_password</c>).
    /// </summary>
    private static bool LooksLikeAPlaceholder(string configured)
    {
        var trimmed = configured.Trim();

        return trimmed.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
               || trimmed.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase)
               || trimmed.Equals("changeme", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the console would sign with the same bytes as the clinics.
    ///
    /// <para>Resolving the clinic key can itself throw or generate a file on a deployment that has no local
    /// auth configured, and a comparison must never be the thing that takes startup down — so a failure to read
    /// it is treated as « cannot be the same key » rather than propagated.</para>
    /// </summary>
    private static bool SharesTheClinicSigningKey(IConfiguration configuration, byte[] consoleKey)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                consoleKey, LocalAuthConfig.ResolveSigningKey(configuration));
        }
        catch
        {
            return false;
        }
    }
}
