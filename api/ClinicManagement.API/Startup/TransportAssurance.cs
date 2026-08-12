using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Security;
using Npgsql;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Refuses to start a hosted deployment whose internal hops are not encrypted and verified
/// (hosted-security-hardening Part 2, FR-2.5). Runs <b>before the host runs</b>, beside the empty-connection-
/// string <c>return 1</c> it is modelled on.
///
/// <para><b>Gated on the deployment KIND, never on whether a certificate happens to be present</b>: a guard
/// that switches itself off when its subject is missing is not a guard. Absent, unreadable and not-yet-valid
/// certificates are three separate refusals, each naming the file <i>and</i> the setting.</para>
///
/// <para>⚠️ <b><c>!SelfHostsFrontDoor</c> — BOTH hosted kinds, not <c>HostedMultiTenant</c> alone</b>, because
/// the configuration reaches both: <c>docker-compose.hosted.yml</c> <c>extends</c>
/// <c>docker-compose.prod.yml</c>'s infrastructure and <c>deploy/postgres/Dockerfile</c> is shared, so the
/// internal CA, <c>ssl=on</c> and the hostssl-only <c>pg_hba.conf</c> land on <c>CloudBrowser</c> too. A check
/// gated one kind narrower than its own configuration means a CloudBrowser deployment whose connection string
/// was missed fails at the <i>first query</i> instead of at startup — transit failing open. Transit is
/// therefore the fifth of this feature's global changes. <c>SelfHostedLan</c> is untouched: it serves its own
/// in-process front door and reaches PostgreSQL on the same machine.</para>
///
/// <para>⚠️ <b>Every problem is reported, not the first.</b> An operator restarting a container once per
/// misconfiguration is a loop measured in minutes, and the four settings here are usually wrong together —
/// they are set together.</para>
///
/// <para>⚠️ <b>The connection string is PARSED, never pattern-matched.</b> Npgsql accepts several spellings of
/// the same keyword and rejects <c>sslmode=verify-full</c>, libpq's form, outright — so a substring check
/// would pass a string the driver will not honour, or fail one it will. Feeding the real value through the
/// real parser also makes <c>TransportConfigurationTests</c> able to assert the compose files' own strings.</para>
/// </summary>
public static class TransportAssurance
{
    /// <summary>Config key holding the database connection string, named in every refusal about it.</summary>
    public const string ConnectionStringKey = "ConnectionStrings:DefaultConnection";

    /// <summary>Config key turning object-store TLS on, named in the refusal about it.</summary>
    public const string MinioUseSslKey = "MinIO:UseSSL";

    /// <summary>The only <see cref="SslMode"/> that verifies the server's identity as well as encrypting.</summary>
    public const SslMode RequiredSslMode = SslMode.VerifyFull;

    /// <summary>
    /// The outcome. <see cref="Problems"/> is empty exactly when the deployment may start; each entry is an
    /// operator-facing French sentence naming what to change and where.
    /// </summary>
    public sealed record Result(bool Applies, IReadOnlyList<string> Problems)
    {
        public bool IsSatisfied => Problems.Count == 0;
    }

    /// <summary>
    /// Inspects the transit configuration as of <paramref name="nowUtc"/>. Pure: it opens no connection and
    /// reads no clock, so the not-yet-valid and expired cases are testable.
    /// </summary>
    public static Result Inspect(
        IConfiguration configuration,
        DeploymentProfile profile,
        DateTime nowUtc,
        InternalCertificate.Store? store = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.SelfHostsFrontDoor)
        {
            return new Result(Applies: false, Array.Empty<string>());
        }

        var problems = new List<string>();

        InspectDatabase(configuration, nowUtc, store, problems);
        InspectObjectStore(configuration, nowUtc, store, problems);

        return new Result(Applies: true, problems);
    }

    private static void InspectDatabase(
        IConfiguration configuration,
        DateTime nowUtc,
        InternalCertificate.Store? store,
        List<string> problems)
    {
        var connectionString = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            problems.Add(
                $"{ConnectionStringKey} est absent : la connexion à la base de données doit être chiffrée et "
                + $"vérifiée (SSL Mode={RequiredSslMode}).");
            return;
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            problems.Add(
                $"{ConnectionStringKey} est illisible : {ex.Message} Attendu, entre autres, "
                + $"« SSL Mode={RequiredSslMode};Root Certificate=<fichier> ».");
            return;
        }

        if (builder.SslMode != RequiredSslMode)
        {
            problems.Add(
                $"{ConnectionStringKey} utilise SSL Mode={builder.SslMode} : seul {RequiredSslMode} vérifie "
                + "l'identité du serveur en plus de chiffrer. Ajoutez « SSL Mode=VerifyFull » à la chaîne de "
                + "connexion (docker-compose : ConnectionStrings__DefaultConnection).");
        }

        var inspection = InternalCertificate.Inspect(builder.RootCertificate, nowUtc, store);
        if (!inspection.IsUsable)
        {
            problems.Add(
                $"Le certificat racine interne de la base de données est inutilisable : {inspection.Detail}. "
                + $"Renseignez « Root Certificate=<fichier> » dans {ConnectionStringKey} et vérifiez que le "
                + "volume internal_certs est monté (docker-compose : internal_certs:/certs:ro).");
        }
    }

    private static void InspectObjectStore(
        IConfiguration configuration,
        DateTime nowUtc,
        InternalCertificate.Store? store,
        List<string> problems)
    {
        // Read by hand rather than with GetValue<bool>, which THROWS on a value it cannot convert — a typo in
        // this key would then crash with a binding error instead of the refusal that names the key.
        var configured = configuration[MinioUseSslKey];
        var useSsl = bool.TryParse(configured?.Trim(), out var parsed) && parsed;

        if (!useSsl)
        {
            problems.Add(
                $"{MinioUseSslKey} vaut « {configured ?? "(absent)"} » : la connexion au stockage d'objets doit "
                + $"être chiffrée. Mettez {MinioUseSslKey} à « true » (docker-compose : MinIO__UseSSL).");
        }

        var inspection = InternalCertificate.Inspect(
            configuration[InternalCertificate.MinioRootCertificateKey], nowUtc, store);
        if (!inspection.IsUsable)
        {
            problems.Add(
                $"Le certificat racine interne du stockage d'objets est inutilisable : {inspection.Detail}. "
                + $"Renseignez {InternalCertificate.MinioRootCertificateKey} (docker-compose : "
                + "MinIO__RootCertificate) et vérifiez que le volume internal_certs est monté.");
        }
    }

    /// <summary>
    /// The operator-facing block written to the console, the log and — on Windows — the Event Log when the
    /// check refuses. One message rather than N log lines, so a container's last output holds all of it.
    /// </summary>
    public static string RefusalMessage(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var lines = new List<string>
        {
            "Démarrage refusé : les échanges internes de ce déploiement ne sont pas chiffrés et vérifiés.",
        };
        lines.AddRange(result.Problems.Select(problem => $"  - {problem}"));
        lines.Add(
            "Aucune donnée de patient ne doit circuler en clair sur le réseau interne. Corrigez les points "
            + "ci-dessus puis redémarrez ; voir deploy/README.md, section « Transit interne ».");

        return string.Join(Environment.NewLine, lines);
    }
}
