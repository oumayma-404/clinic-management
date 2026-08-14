using ClinicManagement.Infrastructure.Deployment;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Refuses to start a hosted deployment that would send a clinic's records to a host the operator has not
/// declared. The residency counterpart of <see cref="TransportAssurance"/>: that one asks « is this hop
/// encrypted? », this one asks « where does this data end up? ».
///
/// <para><b>Why it exists.</b> Under Tunisian law (loi organique <b>2004-63</b>, art. 51–52) transferring
/// personal data abroad needs <b>prior INPDP authorization</b>, health data is separately sensitive, and the
/// art. 90 exposure lands on the <i>clinic</i> — the <c>responsable du traitement</c> — not on the vendor. So
/// where a clinic's data is stored is a legal decision, and it must not be reachable by copying a template.
/// <c>deploy/.env.hosted.example</c> shipped <c>WALG_S3_ENDPOINT=https://s3.us-west-002.backblazeb2.com</c> as
/// its default, which meant an operator who changed nothing continuously shipped every write-ahead-log segment
/// — i.e. every patient write — to Oregon.</para>
///
/// <para>⚠️ <b>An explicit declaration, never geolocation.</b> Resolving a host to a country at startup is a
/// DNS lookup away from failing closed on a network hiccup, and a CDN address answers honestly in a dozen
/// jurisdictions at once. So this is the closed-set pattern <c>Features/Platform/PlatformReadShape</c> already
/// uses for field names: the operator states the hosts data may reach, and anything outside the set refuses. It
/// cannot be right by accident, and it cannot be wrong silently.</para>
///
/// <para>⚠️ <b>Opt-in, because an empty list means « nobody has decided » rather than « nothing is allowed ».</b>
/// Refusing every existing deployment on the day this shipped would teach operators to switch it off, which is
/// exactly how <c>Security:EnforceCsp</c> spent a release inert. Absent ⇒ <see cref="Result.Applies"/> is false
/// and <c>Program.cs</c> warns on <b>every</b> boot naming the key — the same treatment
/// <c>Security:AllowUnverifiedInternalTls</c> gets, and for the same reason.</para>
///
/// <para>⚠️ <b>What it can see is not everything, and it says so rather than implying otherwise.</b> The nightly
/// backup's destination is an <b>rclone remote</b> (<c>offsite:clinic-backups</c>) whose real host lives in
/// <c>deploy/rclone/rclone.conf</c>, a file this process never reads — and it runs in a <i>sibling container</i>
/// anyway. Such a destination is reported as <b>unverified</b>, never as satisfied: a guard that quietly passes
/// what it cannot measure is worse than no guard, because it converts « unknown » into « checked ».</para>
/// </summary>
public static class DataResidencyAssurance
{
    /// <summary>
    /// The hosts this deployment's data may reach, as <c>Residency:AllowedEgressHosts:0</c>, <c>:1</c>, …
    /// Empty ⇒ the check does not apply and the boot warning fires.
    /// </summary>
    public const string AllowedEgressHostsKey = "Residency:AllowedEgressHosts";

    /// <summary>Where WAL-G ships continuous write-ahead-log segments — a full URL, so its host is checkable.</summary>
    public const string PitrEndpointKey = "Backup:PitrEndpoint";

    /// <summary>The nightly rclone destination. A remote NAME, so its host is deliberately NOT derivable here.</summary>
    public const string BackupRemoteKey = "Backup:Remote";

    /// <summary>The object store. Internal on a compose deployment; a real host when it is somebody else's.</summary>
    public const string MinioEndpointKey = "MinIO:Endpoint";

    /// <summary>
    /// The outcome. <see cref="Problems"/> empty ⇒ the deployment may start. <see cref="Unverified"/> is a
    /// separate list on purpose: those are destinations this process is structurally unable to check, and an
    /// operator must be told about them without the deployment being blocked on a fact nothing here can learn.
    /// </summary>
    public sealed record Result(
        bool Applies,
        IReadOnlyList<string> Problems,
        IReadOnlyList<string> Unverified)
    {
        public bool IsSatisfied => Problems.Count == 0;
    }

    /// <summary>The declared allow-list, trimmed and lower-cased; empty when nothing is declared.</summary>
    public static IReadOnlyList<string> AllowedHosts(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(AllowedEgressHostsKey)
            .GetChildren()
            .Select(child => child.Value?.Trim().ToLowerInvariant())
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!)
            .ToArray();
    }

    /// <summary>
    /// Inspects the configured egress destinations. Pure: it opens no connection, resolves no name and reads no
    /// clock, so every case is testable and a DNS outage can never turn this into a failed boot.
    /// </summary>
    public static Result Inspect(IConfiguration configuration, DeploymentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(profile);

        // A clinic's own PC serving its own LAN has no residency question to answer: the data is already on the
        // premises, and its backup destination is a folder on that machine. Same boundary TransportAssurance
        // draws, and for the same reason.
        if (profile.SelfHostsFrontDoor)
        {
            return new Result(Applies: false, Array.Empty<string>(), Array.Empty<string>());
        }

        var allowed = AllowedHosts(configuration);
        if (allowed.Count == 0)
        {
            return new Result(Applies: false, Array.Empty<string>(), Array.Empty<string>());
        }

        var problems = new List<string>();
        var unverified = new List<string>();

        InspectUrlDestination(
            configuration[PitrEndpointKey],
            PitrEndpointKey,
            "l'archivage continu des journaux de transactions (WAL-G), qui contient chaque écriture de chaque "
            + "dossier patient",
            "WALG_S3_ENDPOINT",
            allowed,
            problems);

        InspectUrlDestination(
            configuration[MinioEndpointKey],
            MinioEndpointKey,
            "le stockage d'objets (radiographies, documents, PDF)",
            "MinIO__Endpoint",
            allowed,
            problems);

        // ⚠️ Reported, never resolved. `offsite:clinic-backups` names an rclone remote; the host behind it is in
        // deploy/rclone/rclone.conf, which this process does not read and which belongs to another container.
        // Pretending to have checked it is the one outcome worse than admitting we cannot.
        var remote = configuration[BackupRemoteKey]?.Trim();
        if (!string.IsNullOrEmpty(remote))
        {
            unverified.Add(
                $"{BackupRemoteKey} vaut « {remote} » : c'est un *remote* rclone, dont l'hôte réel est défini "
                + "dans deploy/rclone/rclone.conf et ne peut pas être vérifié depuis cette application. "
                + "Vérifiez à la main que cette destination est en Tunisie — c'est une sauvegarde complète de "
                + "la base.");
        }

        return new Result(Applies: true, problems, unverified);
    }

    private static void InspectUrlDestination(
        string? configured,
        string key,
        string whatItCarries,
        string composeVariable,
        IReadOnlyList<string> allowed,
        List<string> problems)
    {
        var value = configured?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var host = HostOf(value);
        if (host is null)
        {
            problems.Add(
                $"{key} vaut « {value} », dont l'hôte est illisible. Attendu une URL ou « hôte:port » "
                + $"(docker-compose : {composeVariable}).");
            return;
        }

        // ⚠️ A dotless host is a container name on the compose network (`minio`, `postgres`) — traffic that never
        // leaves the machine, so it is not egress and must not have to be allow-listed. Forcing an operator to
        // declare `minio` would train them to add whatever the refusal names, which is how an allow-list stops
        // being a decision.
        if (!host.Contains('.'))
        {
            return;
        }

        if (!allowed.Contains(host))
        {
            problems.Add(
                $"{key} envoie {whatItCarries} vers « {host} », qui ne figure pas dans "
                + $"{AllowedEgressHostsKey}. Soit cette destination est hors de Tunisie et doit être changée, "
                + $"soit elle est légitime et doit être déclarée (docker-compose : {composeVariable}).");
        }
    }

    /// <summary>
    /// The host of a URL or of a bare <c>host:port</c>. Returns null when neither form parses — reported as a
    /// problem rather than skipped, since an unreadable destination is not a safe one.
    /// </summary>
    private static string? HostOf(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && !string.IsNullOrEmpty(absolute.Host))
        {
            return absolute.Host.ToLowerInvariant();
        }

        // `minio:9000` and `s3.example.tn:443` are not absolute URIs. A scheme makes them parseable, and the
        // result is discarded unless it yields a host — so a value that is neither shape still returns null.
        if (Uri.TryCreate($"https://{value}", UriKind.Absolute, out var relative)
            && !string.IsNullOrEmpty(relative.Host))
        {
            return relative.Host.ToLowerInvariant();
        }

        return null;
    }

    /// <summary>
    /// The operator-facing block written when the check refuses. One message rather than N log lines, so a
    /// container's last output holds all of it — <see cref="TransportAssurance.RefusalMessage"/>'s shape.
    /// </summary>
    public static string RefusalMessage(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var lines = new List<string>
        {
            "Démarrage refusé : ce déploiement enverrait des données de patients vers un hôte non déclaré.",
        };
        lines.AddRange(result.Problems.Select(problem => $"  - {problem}"));
        lines.Add(
            "Le lieu d'hébergement des données de santé est une décision juridique (loi organique 2004-63, "
            + "art. 51-52 : tout transfert à l'étranger exige l'autorisation préalable de l'INPDP). Corrigez "
            + "les points ci-dessus puis redémarrez ; voir deploy/README.md, section « Résidence des données ».");

        return string.Join(Environment.NewLine, lines);
    }
}
