using ClinicManagement.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;

namespace ClinicManagement.Infrastructure.Deployment;

/// <summary>
/// The three topologies this product can be deployed as. Two questions distinguish them — where the front door
/// and the data live, and who issues the tokens — and every other difference follows from those.
/// </summary>
public enum DeploymentKind
{
    /// <summary>The clinic's own Windows PC serves the LAN: its data, its disk, its self-signed certificate.</summary>
    SelfHostedLan,

    /// <summary>One hosted backend serving many clinics, each running the desktop client, on the product's own accounts.</summary>
    HostedMultiTenant,

    /// <summary>One hosted backend reached by a browser, with Auth0 as the identity provider.</summary>
    CloudBrowser
}

/// <summary>
/// The resolved deployment profile: <b>one capability per question</b> the deployment has to answer.
///
/// <para><b>What this replaces.</b> <see cref="LocalAuthConfig.IsLocalMode"/> was a single boolean answering a
/// dozen unrelated questions at ~30 call sites — the login provider, the storage backend, the authorization
/// fallback, certificate self-signing, Windows-service hosting, migration timing, the HSTS default, the
/// connectivity probe, the YARP front door, the log path and every console verb's gate. Two profiles happened to
/// agree on all of them, so one flag was enough. A third does not, and under one flag it would get half of them
/// right <i>by accident</i>. Each site now asks what it actually means.</para>
///
/// <para><b>⚠️ This is deliberately NOT the pattern <c>LEARNINGS.md</c> warns about.</b> That lesson — « gate
/// mode-invariant guards on the mode flag, not a capability flag » — came from <c>httpsConfigured</c>, a value
/// derived from <i>configuration</i> (a cert path being present) that merely <i>correlated</i> with the mode, and
/// so silently changed Cloud behaviour when it was set. Every capability here is derived from the resolved
/// <see cref="Kind"/> and from nothing else: no operator setting can flip one without changing the profile
/// itself. <c>DeploymentProfileTests</c> asserts the two pre-existing kinds reproduce today's
/// <c>IsLocalMode</c> truth table exactly, which is what makes that more than a claim.</para>
/// </summary>
public sealed class DeploymentProfile
{
    /// <summary>Configuration key naming the profile explicitly; absent means « derive it from <c>Auth:Mode</c> ».</summary>
    public const string ProfileKey = "Deployment:Profile";

    private DeploymentProfile(
        DeploymentKind kind,
        bool usesLocalAccounts,
        bool failClosedAuthz,
        bool enforcesTokenState,
        bool usesDiskStorage,
        bool selfHostsFrontDoor,
        bool selfSignsCertificate,
        bool runsAsWindowsService,
        bool defersMigrations,
        bool runsStartupBackfills,
        bool exposesTrustEndpoints,
        bool hasLocalDbTooling,
        bool exposesMetaOnboarding)
    {
        Kind = kind;
        UsesLocalAccounts = usesLocalAccounts;
        FailClosedAuthz = failClosedAuthz;
        EnforcesTokenState = enforcesTokenState;
        UsesDiskStorage = usesDiskStorage;
        SelfHostsFrontDoor = selfHostsFrontDoor;
        SelfSignsCertificate = selfSignsCertificate;
        RunsAsWindowsService = runsAsWindowsService;
        DefersMigrations = defersMigrations;
        RunsStartupBackfills = runsStartupBackfills;
        ExposesTrustEndpoints = exposesTrustEndpoints;
        HasLocalDbTooling = hasLocalDbTooling;
        ExposesMetaOnboarding = exposesMetaOnboarding;
    }

    /// <summary>Which topology this install is.</summary>
    public DeploymentKind Kind { get; }

    /// <summary>The product issues its own email+password JWTs, rather than validating Auth0's.</summary>
    public bool UsesLocalAccounts { get; }

    /// <summary>Authorization installs a <c>FallbackPolicy = RequireAuthenticatedUser()</c>, so anonymous-by-omission cannot exist.</summary>
    public bool FailClosedAuthz { get; }

    /// <summary>Account state (deactivated, forced password change) is re-checked per request, because the app-issued JWT is stateless.</summary>
    public bool EnforcesTokenState { get; }

    /// <summary>Blobs live on the server's own disk rather than in MinIO.</summary>
    public bool UsesDiskStorage { get; }

    /// <summary>Kestrel is the single browser-facing endpoint and reverse-proxies the co-located Next server.</summary>
    public bool SelfHostsFrontDoor { get; }

    /// <summary>HTTPS trust material is self-generated into <c>.local/</c> on first boot; there is no public CA.</summary>
    public bool SelfSignsCertificate { get; }

    /// <summary>The process runs as an auto-starting Windows service, so every path it uses is install-relative and not CWD-relative.</summary>
    public bool RunsAsWindowsService { get; }

    /// <summary>Migrations run in a post-startup hosted service instead of inline, to stay inside the SCM's ~30 s start timeout.</summary>
    public bool DefersMigrations { get; }

    /// <summary>The inline startup block owes the per-clinic catalog seed and the clinic-admin backfill.</summary>
    public bool RunsStartupBackfills { get; }

    /// <summary>The LAN trust page and the server-side connectivity probe are served — both exist only for a clinic-hosted box.</summary>
    public bool ExposesTrustEndpoints { get; }

    /// <summary>PostgreSQL client tooling (<c>pg_dump</c>/<c>pg_restore</c>) is present, so the report and backup verbs can run.</summary>
    public bool HasLocalDbTooling { get; }

    /// <summary>Meta's WhatsApp Embedded Signup is reachable, so a clinic can connect its own WhatsApp Business account.</summary>
    public bool ExposesMetaOnboarding { get; }

    /// <summary>
    /// Resolves the profile from configuration.
    ///
    /// <para><c>Deployment:Profile</c> names it explicitly. When the key is <b>absent</b> the profile is derived
    /// from <c>Auth:Mode</c> exactly as the old boolean was (<c>Local</c> → <see cref="DeploymentKind.SelfHostedLan"/>,
    /// anything else → <see cref="DeploymentKind.CloudBrowser"/>), so every existing install and all seven console
    /// verbs keep working with no config edit. A value that is present but unrecognised <b>throws</b>: falling back
    /// would hand a hosted deployment Auth0 login and no local accounts, silently, which is the failure this key
    /// exists to make impossible.</para>
    /// </summary>
    public static DeploymentProfile Resolve(IConfiguration configuration)
    {
        var configured = configuration[ProfileKey];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return For(LocalAuthConfig.IsLocalMode(configuration)
                ? DeploymentKind.SelfHostedLan
                : DeploymentKind.CloudBrowser);
        }

        if (!Enum.TryParse<DeploymentKind>(configured.Trim(), ignoreCase: true, out var kind))
        {
            throw new InvalidOperationException(
                $"{ProfileKey} = '{configured}' is not a known deployment profile. Use one of: "
                + string.Join(", ", Enum.GetNames<DeploymentKind>())
                + $". Remove the key to derive the profile from Auth:Mode as before.");
        }

        return For(kind);
    }

    /// <summary>
    /// The capability matrix. <b>The two pre-existing kinds reproduce today's <c>IsLocalMode</c> truth table
    /// exactly</b> — that is the contract that makes this refactor safe to land, and
    /// <c>DeploymentProfileTests</c> holds it.
    /// </summary>
    public static DeploymentProfile For(DeploymentKind kind) => kind switch
    {
        DeploymentKind.SelfHostedLan => new DeploymentProfile(
            kind,
            usesLocalAccounts: true,
            failClosedAuthz: true,
            enforcesTokenState: true,
            usesDiskStorage: true,
            selfHostsFrontDoor: true,
            selfSignsCertificate: true,
            runsAsWindowsService: true,
            defersMigrations: true,
            // False because the work is deferred, not because the profile does not owe it: DeferredStartupService
            // runs the catalog seed instead. ⚠️ It does NOT run the clinic-admin backfill — a pre-existing gap
            // this refactor deliberately preserves rather than silently changing Local behaviour.
            runsStartupBackfills: false,
            exposesTrustEndpoints: true,
            hasLocalDbTooling: true,
            exposesMetaOnboarding: false),

        DeploymentKind.HostedMultiTenant => new DeploymentProfile(
            kind,
            usesLocalAccounts: true,
            failClosedAuthz: true,
            enforcesTokenState: true,
            usesDiskStorage: false,
            selfHostsFrontDoor: false,
            selfSignsCertificate: false,
            runsAsWindowsService: false,
            // Not a Windows service, so no SCM start timeout to stay inside — but the backfills are still owed.
            defersMigrations: false,
            runsStartupBackfills: true,
            exposesTrustEndpoints: false,
            hasLocalDbTooling: false,
            exposesMetaOnboarding: true),

        DeploymentKind.CloudBrowser => new DeploymentProfile(
            kind,
            usesLocalAccounts: false,
            failClosedAuthz: false,
            enforcesTokenState: false,
            usesDiskStorage: false,
            selfHostsFrontDoor: false,
            selfSignsCertificate: false,
            runsAsWindowsService: false,
            defersMigrations: false,
            runsStartupBackfills: true,
            exposesTrustEndpoints: false,
            hasLocalDbTooling: false,
            exposesMetaOnboarding: true),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled deployment kind.")
    };
}
