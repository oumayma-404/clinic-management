using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Hands out the secret the audit chain is keyed on (<c>hosted-security-hardening</c> FR-4.1). A seam rather than
/// a static call so the appender and the schema reader take it as a dependency and a test can supply its own.
/// </summary>
public interface IAuditChainKeyProvider
{
    /// <summary>The key. Resolved once at startup; the same bytes for the life of the process.</summary>
    byte[] Key { get; }
}

/// <summary>
/// Resolves <c>Audit:ChainKey</c>, and <b>refuses to start</b> where the deployment cannot generate one for
/// itself (FR-4.1, <c>LocalDataProtection</c>'s precedent).
///
/// <para><b>Three cases, and the middle one is why this is not simply « required ».</b></para>
/// <list type="number">
///   <item>An explicit <c>Audit:ChainKey</c> — base64 or raw, at least
///   <see cref="AuditChain.MinimumKeyBytes"/> bytes. Always wins.</item>
///   <item>No key, but the deployment is a clinic's own PC (<c>SelfHostsFrontDoor</c>) or a developer machine:
///   generate 64 bytes and persist them beside the local signing key, exactly as <c>LocalAuthConfig</c> does.
///   Requiring an operator-set value there would break every existing install on upgrade and hand a dentist a
///   startup failure over a key nobody but this process ever reads.</item>
///   <item>No key on a hosted deployment: <b>throw</b>, naming the key. There is an operator, the key must
///   survive a container being replaced, and a generated one would live on the container layer — so it would
///   work, and then the first redeploy would silently un-verify every entry written before it. That failure has
///   the same signature as tampering, which is the one thing this feature must not manufacture.</item>
/// </list>
///
/// <para>⚠️ <b>Deliberately not the Data Protection ring.</b> Part C re-protects that ring and FR-3.9 makes it
/// the thing a restore may fail to read; a chain whose verification died with it would be unfalsifiable at
/// exactly the moment somebody wanted to check it.</para>
///
/// <para>⚠️ <b>The Development exemption is DEV-9's, not a new one.</b> <c>appsettings.Development.json</c>
/// selects <c>HostedMultiTenant</c> on purpose — it is the only profile whose public signup door is open — so
/// without it <c>dotnet run</c> and <c>dotnet ef migrations add</c> would refuse to start on a fresh clone for
/// every developer, which is how a guard gets switched off wholesale.</para>
/// </summary>
public sealed class AuditChainKeyProvider : IAuditChainKeyProvider
{
    public const string ConfigKey = "Audit:ChainKey";

    /// <summary>Where a self-generated key is persisted, relative to the install directory.</summary>
    public const string KeyFileName = "audit-chain-key";

    private const int GeneratedKeyBytes = 64;

    private static readonly object KeyFileLock = new();

    private readonly byte[] _key;

    public AuditChainKeyProvider(IConfiguration configuration)
        : this(configuration, DeploymentProfile.Resolve(configuration))
    {
    }

    public AuditChainKeyProvider(IConfiguration configuration, DeploymentProfile profile)
    {
        _key = Resolve(configuration, profile);
    }

    public byte[] Key => _key;

    /// <summary>
    /// The three cases above, as one function so a test can drive it without a container. Returns the key or
    /// throws with the operator-facing sentence.
    /// </summary>
    public static byte[] Resolve(IConfiguration configuration, DeploymentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(profile);

        var configured = configuration[ConfigKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Decode(configured.Trim());
        }

        if (profile.SelfHostsFrontDoor || IsDevelopment(configuration))
        {
            return LoadOrGenerate(configuration);
        }

        throw new InvalidOperationException(
            $"La clé de chaînage du journal d'audit est absente : renseignez « {ConfigKey} » "
            + $"(au moins {AuditChain.MinimumKeyBytes} octets, en base64). Sans elle, le journal ne peut pas être "
            + "protégé contre une modification, et une clé engendrée automatiquement disparaîtrait au premier "
            + "redéploiement — ce qui rendrait toutes les entrées déjà écrites invérifiables. "
            + "Voir deploy/KEY-CUSTODY.md.");
    }

    private static bool IsDevelopment(IConfiguration configuration) =>
        string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Base64 where it decodes to enough bytes, else the raw UTF-8. Both forms are accepted because an operator
    /// setting an environment variable by hand will not necessarily reach for base64, and refusing a long
    /// passphrase would be a refusal over spelling.
    /// </summary>
    private static byte[] Decode(string value)
    {
        var buffer = new byte[value.Length];
        if (Convert.TryFromBase64String(value, buffer, out var written) && written >= AuditChain.MinimumKeyBytes)
        {
            return buffer[..written];
        }

        var raw = Encoding.UTF8.GetBytes(value);
        if (raw.Length >= AuditChain.MinimumKeyBytes)
        {
            return raw;
        }

        throw new InvalidOperationException(
            $"« {ConfigKey} » est trop courte : au moins {AuditChain.MinimumKeyBytes} octets sont requis "
            + $"(la valeur fournie en fait {raw.Length}).");
    }

    private static byte[] LoadOrGenerate(IConfiguration configuration)
    {
        var path = configuration["Audit:ChainKeyPath"] ?? LocalInstallPaths.LocalFile(KeyFileName);

        lock (KeyFileLock)
        {
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
                        // Naming the file and saying what deleting it costs, rather than silently regenerating:
                        // a fresh key makes every entry written under the old one read as altered.
                        throw new InvalidOperationException(
                            $"Le fichier de clé de chaînage « {path} » est illisible. Restaurez-le depuis une "
                            + "sauvegarde ; le supprimer engendre une nouvelle clé, et toutes les entrées déjà "
                            + "écrites deviendront invérifiables.");
                    }
                }
            }

            var key = RandomNumberGenerator.GetBytes(GeneratedKeyBytes);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, Convert.ToBase64String(key));
            return key;
        }
    }
}
