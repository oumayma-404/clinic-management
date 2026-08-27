using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Security;
using ClinicManagement.Infrastructure.Storage;
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
/// <para>⚠️ <b>Gated on <c>!SelfHostsFrontDoor</c>, deliberately, and not on <c>HostedMultiTenant</c>.</b> The
/// two are the same set today — <c>CloudBrowser</c> was the other hosted kind and is retired — but the gate
/// states the reason rather than the roster: what this check needs is a deployment whose PostgreSQL it reaches
/// over a network it does not own, and that is what <c>SelfHostsFrontDoor</c> answers. It was written this way
/// after a narrower gate let a hosted deployment whose connection string was missed fail at the <i>first
/// query</i> instead of at startup — transit failing open — and naming the kind now would re-introduce exactly
/// that coupling for whatever hosted kind comes next. <c>SelfHostedLan</c> is untouched: it serves its own
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
    /// Accepts an <b>encrypted but unverified</b> database hop (<see cref="SslMode.Require"/>) on a host that
    /// publishes no CA certificate and offers no way to mount one.
    ///
    /// <para><b>This is a reduction, and it is opt-in, non-default and logged on every boot so it cannot become
    /// invisible.</b> What is kept: the traffic is still encrypted, so a passive tap on the internal network
    /// reads nothing. What is given up: <i>identity</i> — <see cref="SslMode.Require"/> accepts whatever
    /// certificate it is handed, so an impostor between this process and the database would not be detected.</para>
    ///
    /// <para><b>Why it exists.</b> <see cref="RequiredSslMode"/> needs a root-certificate <b>file</b>, and a
    /// managed platform's free tier offers neither a durable disk nor a published CA for its own database
    /// (verified against Render's documentation: external connections use "Render-managed TLS certificates",
    /// with no CA download and nothing stated about internal ones). The alternative was shipping nothing, or
    /// pretending the check passed.</para>
    ///
    /// <para>⚠️ <b>It never accepts an UNENCRYPTED hop.</b> <c>Disable</c>, <c>Allow</c> and <c>Prefer</c> stay
    /// refused with this set, because « no patient data crosses the internal network in clear » is the promise
    /// this class exists for and it is not the promise being traded away here.</para>
    ///
    /// <para>⚠️ <b>Temporary by intent</b> — <c>follow-up/render-free-tier-transit-relaxation.md</c> records it as
    /// the thing to remove on a host that can mount a certificate.</para>
    /// </summary>
    public const string AllowUnverifiedTlsKey = "Security:AllowUnverifiedInternalTls";

    /// <summary>
    /// Whether an unencrypted internal hop is tolerated here. True in <c>Development</c> alone, exactly as
    /// <see cref="ClinicManagement.Infrastructure.Security.LocalDataProtection.TolerateUnprotectedKeyRing"/> and
    /// <c>MinioCredentials.TolerateUnconfigured</c> decide the same question for the key ring and for object-store
    /// credentials.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Without this the product could not be run locally at all.</b> This check applies to every profile
    /// that is not <c>SelfHostedLan</c> — which includes <c>HostedMultiTenant</c>, the profile
    /// <c>appsettings.Development.json</c> selects for local development. It then demands
    /// <c>SSL Mode=VerifyFull</c> plus an internal root certificate, while <c>docker-compose.yml</c> runs
    /// PostgreSQL with <c>ssl = off</c> and MinIO with no TLS at all. Startup was refused on every developer
    /// machine, so the API actually running was whatever binary predated the guard — a stale build that silently
    /// diverged from the source for days. The two guards either side of this one already exempt Development for
    /// precisely that reason; this one did not, and the inconsistency was the defect.
    /// <para>The exemption is <b>Development only</b>: every deployed environment still refuses, so the promise
    /// that no patient data crosses the internal network in clear is unchanged where it means anything.</para>
    /// </remarks>
    public static bool TolerateUnencryptedTransit(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var environmentName =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? configuration["Environment"];
        return string.Equals(environmentName?.Trim(), "Development", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether the operator explicitly accepted an encrypted-but-unverified internal hop.</summary>
    public static bool AllowsUnverifiedTls(IConfiguration configuration) =>
        bool.TryParse(configuration[AllowUnverifiedTlsKey]?.Trim(), out var value) && value;

    /// <summary>
    /// The <see cref="SslMode"/>s that at least encrypt. Anything outside this set is refused whatever
    /// <see cref="AllowUnverifiedTlsKey"/> says.
    /// </summary>
    private static readonly SslMode[] EncryptingModes = { SslMode.Require, SslMode.VerifyCA, SslMode.VerifyFull };

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

        var allowUnverified = AllowsUnverifiedTls(configuration);

        if (builder.SslMode != RequiredSslMode)
        {
            // The relaxation trades IDENTITY, never ENCRYPTION: a mode that does not guarantee a cipher is
            // refused whatever the operator set, because « rien en clair sur le réseau interne » is the promise
            // this class exists for and is not the one being traded.
            if (!allowUnverified || !EncryptingModes.Contains(builder.SslMode))
            {
                problems.Add(
                    $"{ConnectionStringKey} utilise SSL Mode={builder.SslMode} : seul {RequiredSslMode} vérifie "
                    + "l'identité du serveur en plus de chiffrer. Ajoutez « SSL Mode=VerifyFull » à la chaîne de "
                    + "connexion (docker-compose : ConnectionStrings__DefaultConnection)."
                    + (allowUnverified
                        ? $" ⚠️ {AllowUnverifiedTlsKey} est activé, mais ce mode ne chiffre pas : seuls "
                          + "Require, VerifyCA et VerifyFull sont acceptés."
                        : string.Empty));
            }
        }

        // Where the operator accepted an unverified hop there is, by definition, no root certificate to name —
        // demanding one anyway would make the acceptance unusable and is the check this relaxation is about.
        if (!allowUnverified)
        {
            var inspection = InternalCertificate.Inspect(builder.RootCertificate, nowUtc, store);
            if (!inspection.IsUsable)
            {
                problems.Add(
                    $"Le certificat racine interne de la base de données est inutilisable : {inspection.Detail}. "
                    + $"Renseignez « Root Certificate=<fichier> » dans {ConnectionStringKey} et vérifiez que le "
                    + "volume internal_certs est monté (docker-compose : internal_certs:/certs:ro).");
            }
        }
    }

    private static void InspectObjectStore(
        IConfiguration configuration,
        DateTime nowUtc,
        InternalCertificate.Store? store,
        List<string> problems)
    {
        // ⚠️ A hop that does not exist cannot be unencrypted. Where no object store is configured at all, this
        // check used to refuse startup over the transit of a connection the deployment never opens — and
        // `AddInfrastructure` already registers a storage stub that throws on use there, so the absence is a
        // known, handled state rather than a misconfiguration. Demanding TLS to nothing blocked exactly the
        // deployments that have not wired object storage up yet.
        if (!MinioCredentials.IsConfigured(
                configuration["MinIO:Endpoint"], configuration["MinIO:AccessKey"], configuration["MinIO:SecretKey"]))
        {
            return;
        }

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

        // Same trade as the database hop: with the relaxation set there is no internal CA to name, and an
        // object store reached over public TLS verifies against the system trust store rather than against a
        // file this deployment mounts.
        if (AllowsUnverifiedTls(configuration))
        {
            return;
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
