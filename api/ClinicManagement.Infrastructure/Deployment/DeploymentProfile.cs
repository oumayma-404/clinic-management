using ClinicManagement.Domain.Enums;
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
        bool exposesMetaOnboarding,
        bool allowsSelfRegistration,
        bool allowsPublicClinicSignup,
        bool servesPlatformConsole,
        bool requiresSubscription,
        bool backsUpItsOwnData,
        bool sellsVendorMessaging,
        bool requiresAdminSecondFactor)
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
        AllowsSelfRegistration = allowsSelfRegistration;
        AllowsPublicClinicSignup = allowsPublicClinicSignup;
        ServesPlatformConsole = servesPlatformConsole;
        RequiresSubscription = requiresSubscription;
        BacksUpItsOwnData = backsUpItsOwnData;
        SellsVendorMessaging = sellsVendorMessaging;
        RequiresAdminSecondFactor = requiresAdminSecondFactor;
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
    /// Staff may mint their own account by typing the clinic's join code (<c>POST /api/auth/register</c>).
    ///
    /// <para>⚠️ Deliberately <b>not</b> <see cref="UsesLocalAccounts"/>, which is what gated it before US-3 and is
    /// true in both account-owning profiles. The clinic code is <b>six characters</b> over a 36-symbol alphabet,
    /// shown on a settings screen and known to everyone who ever worked at the practice. On a LAN that is a gate,
    /// because reaching the endpoint at all means being inside the surgery; over the internet it is a password
    /// everybody has. Where this is false, an admin creates the account instead
    /// (<c>CreateClinicUserCommand</c>) and hands over a one-time password.</para>
    /// </summary>
    public bool AllowsSelfRegistration { get; }

    /// <summary>
    /// A visitor may create their own clinic and admin account from the public internet
    /// (<c>POST /api/auth/signup</c> + <c>/signup/verify</c>), with no operator action at all.
    ///
    /// <para><b>True only for <see cref="DeploymentKind.HostedMultiTenant"/></b>, and each of the other two is a
    /// ✗ for its own reason rather than by default. <see cref="DeploymentKind.SelfHostedLan"/> serves <b>one</b>
    /// clinic from a PC in that clinic's own surgery: a second clinic on it is not a topology, and first-run
    /// <c>setup</c> — loopback-gated, once — is how the one clinic gets created.
    /// <see cref="DeploymentKind.CloudBrowser"/> is multi-clinic but Auth0 owns its identities, so a signup here
    /// would mint a password-backed local account its login path cannot authenticate.</para>
    ///
    /// <para>⚠️ <b>This does not reopen what US-3 closed, and it is not
    /// <see cref="AllowsSelfRegistration"/>.</b> That capability is about <i>joining an existing clinic</i> with
    /// its six-character code — a shared password everyone who ever worked at the practice knows, which is a gate
    /// on a LAN and nothing on the internet — and it stays ✗ here. This one hands out no shared secret at all:
    /// the gate is a fresh 32-byte token delivered to an address the visitor has to control, single-use and
    /// expiring, and it creates a clinic with exactly one member rather than admitting a stranger to a clinic
    /// full of patient records.</para>
    /// </summary>
    public bool AllowsPublicClinicSignup { get; }

    /// <summary>
    /// The vendor's private back-office exists on this deployment — a second identity population, a second Kestrel
    /// listener and exactly one cross-cabinet read (<c>platform-console</c> FR-2).
    ///
    /// <para><b>True for <see cref="DeploymentKind.HostedMultiTenant"/> alone</b>, and each ✗ is its own reason
    /// rather than a default. <see cref="DeploymentKind.SelfHostedLan"/> serves <b>one</b> cabinet from a PC in that
    /// cabinet's own surgery: there is no portfolio to run, and the vendor is not on that network.
    /// <see cref="DeploymentKind.CloudBrowser"/> is multi-clinic, but Auth0 owns its identities and its clinics are
    /// not on the subscription arrangement this console administers.</para>
    ///
    /// <para>⚠️ <b>This decides whether the console <i>may</i> exist; <c>Console:Port</c> decides whether it is
    /// bound.</b> Off means <b>absent</b> — no listener, no reachable route, 404 — never present-and-refusing
    /// (AC-1.8), the same shape <c>Hosting:TrustPort = 0</c> already has. Keeping the port out of the capability is
    /// what preserves this class's own invariant that no operator setting can flip one.</para>
    /// </summary>
    public bool ServesPlatformConsole { get; }

    /// <summary>
    /// A cabinet's right to <b>record new work</b> is a dated entitlement here: 30 free days, then read-only until
    /// the vendor records a payment (<c>clinic-subscription</c> FR-11, AC-7.1–7.3).
    ///
    /// <para><b>True only for <see cref="DeploymentKind.HostedMultiTenant"/></b> — the one topology we host, bill
    /// and can be owed money for. On <see cref="DeploymentKind.SelfHostedLan"/> the data is on the clinic's own PC
    /// and refusing writes would hold their patient records hostage on hardware we do not own;
    /// <see cref="DeploymentKind.CloudBrowser"/> predates this arrangement and its clinics are not on it.</para>
    ///
    /// <para>⚠️ <b>Decided by the kind and by nothing an operator can set</b> (AC-7.3), which is why it is a
    /// capability here rather than a <c>Subscription:Enabled</c> key. <c>TrialDays</c> and the prices <i>are</i>
    /// configuration and live on <c>ISubscriptionPolicy</c>/<c>ISubscriptionPricing</c>; the split is
    /// <see cref="PermitsOsPush"/>'s, and for the same reason — a flag config can flip is the
    /// <c>httpsConfigured</c> trap the class note above says every capability here avoids.</para>
    ///
    /// <para>Where this is false the entitlement still <i>exists</i> — created <b>open-ended</b>, so FR-13's « no
    /// cabinet without one » holds in all three topologies while nothing can ever expire in two of them.</para>
    /// </summary>
    public bool RequiresSubscription { get; }

    /// <summary>
    /// The <b>application</b> backs its own database up — « Sauvegarder maintenant », the hourly
    /// <c>BackupJob</c>, the schedule on the clinic and the staleness alert. Where this is false, protecting the
    /// data is the <i>host's</i> job and the product says so rather than offering a button.
    ///
    /// <para><b>True for <see cref="DeploymentKind.SelfHostedLan"/> alone</b>, and the ✗ on the two hosted kinds is
    /// a decision with two independent reasons behind it, not a default.</para>
    ///
    /// <para><b>(1) There is a real backup there already, and it is better.</b> <c>deploy/docker-compose.prod.yml</c>
    /// runs a <c>backup</c> sidecar on <c>postgres:16-alpine</c> that dumps the database, archives the object store
    /// and pushes both <b>off-server</b> through rclone on a schedule. An in-app dump onto the same host is strictly
    /// weaker than what is already running.</para>
    ///
    /// <para><b>(2) On a shared database an in-app backup is a cross-tenant read.</b> <c>pg_dump</c> takes
    /// <c>--dbname</c> and has no tenant predicate — the whole point of <see cref="DeploymentKind.HostedMultiTenant"/>
    /// is that every cabinet's rows live in one database behind the query filters — so « Dr X clicks Sauvegarder »
    /// would dump <b>every other practice's patients</b>. Nothing today could exfiltrate it (there is no download
    /// endpoint, and the file lands on a disk only the operator can reach), which is exactly why this must be a
    /// capability rather than a comment: the day somebody adds « télécharger la sauvegarde » to that screen, the
    /// leak arrives with it.</para>
    ///
    /// <para>⚠️ <b>What a clinic can still do is unaffected</b>, and that distinction is the whole design: every
    /// <b>CSV export</b> and every PDF stays exactly as it was on all three kinds, because those are per-clinic
    /// reads that go through the tenant filter. « A cabinet can always take its own data out » is served by those,
    /// not by <c>pg_dump</c>.</para>
    ///
    /// <para>⚠️ <b>Derived from the kind, like every capability here.</b> There is deliberately no
    /// <c>Backup:Enabled</c> — a key able to flip this would let a hosted deployment be configured into offering
    /// one cabinet a dump of all the others.</para>
    /// </summary>
    public bool BacksUpItsOwnData { get; }

    /// <summary>
    /// The <b>vendor</b> buys this deployment's WhatsApp messaging capacity centrally and allocates each cabinet a
    /// monthly allowance of reminder messages (<c>vendor-whatsapp-messaging-quota</c> FR-9).
    ///
    /// <para><b>True only for <see cref="DeploymentKind.HostedMultiTenant"/></b>, and each ✗ is its own reason
    /// rather than a default. On <see cref="DeploymentKind.SelfHostedLan"/> the practice owns the machine, the
    /// Meta account and the bill: metering somebody else's WhatsApp spend from their own PC is not a service, and
    /// there is no vendor credit line behind it to meter.
    /// <see cref="DeploymentKind.CloudBrowser"/> predates the arrangement, and its clinics supply their own
    /// WhatsApp credentials through the manual fields this feature closes.</para>
    ///
    /// <para>⚠️ <b>Derived from the kind and from nothing an operator can set</b> (FR-9), like every capability
    /// here. Whether the deployment's own <b>Meta credentials</b> are present is a separate question with a
    /// separate answer — it lives on <c>IVendorMessagingAvailability</c>, which is
    /// <see cref="PermitsOsPush"/>'s split and exists for the same reason: a <c>Messaging:*</c> key able to flip
    /// this would be the <c>httpsConfigured</c> trap the class note above says every capability avoids.</para>
    ///
    /// <para>Where this is false every surface of the feature is <b>absent</b> — no section, no notifications, no
    /// enforcement, no scheduled work, endpoints answering as though they do not exist — and the existing WhatsApp
    /// behaviour is byte-for-byte unchanged (EC-16).</para>
    /// </summary>
    public bool SellsVendorMessaging { get; }

    /// <summary>
    /// A clinic <b>administrator</b> must present a second factor to obtain a session, and must enrol one before
    /// they can obtain their first (<c>hosted-security-hardening</c> FR-1.1–FR-1.3).
    ///
    /// <para><b>True for <see cref="DeploymentKind.HostedMultiTenant"/> alone</b>, and each ✗ is its own decision
    /// rather than a default.</para>
    ///
    /// <para><b><see cref="DeploymentKind.SelfHostedLan"/> ✗ — an administrator locked out with no vendor to call
    /// is worse than the threat.</b> That deployment is one practice's own PC on its own network: reaching the
    /// login form at all means standing in the surgery, and there is nobody to ring when the phone holding the
    /// authenticator is lost or replaced. The three ways back this feature ships all assume somebody else — a
    /// second administrator, or the vendor running a console verb — and on a single-dentist LAN install neither
    /// exists. AC-7 (« no practice is ever locked out of its own records by a control introduced here ») is
    /// therefore unsatisfiable there, which is what decides it.</para>
    ///
    /// <para><b><see cref="DeploymentKind.CloudBrowser"/> ✗ — Auth0 owns those identities.</b> The password is
    /// not ours to gate and the second factor belongs in the identity provider's own policy, where that tenant
    /// already configures it. Enforcing one here would be a second, weaker factor bolted onto a login this
    /// product does not perform.</para>
    ///
    /// <para>⚠️ <b>This is the requirement, not the capability to enrol.</b> A doctor or secretary on any
    /// deployment may enrol voluntarily from « Sécurité » — that surface is unconditional. What this decides is
    /// whether an <i>administrator</i> is refused a session without one, and consequently whether such an
    /// administrator is allowed to <i>disable</i> theirs: an unconditional refusal to disable would strand a
    /// voluntarily-enrolled admin on the two profiles this is ✗ for, which is a control with no way out.</para>
    /// </summary>
    public bool RequiresAdminSecondFactor { get; }

    /// <summary>
    /// May this topology deliver OS push to <paramref name="platform"/> at all? (spec FR-10, AC-51/AC-52.)
    ///
    /// <para><b>Per-platform, not one boolean</b>, because a deployment with a Firebase project and no Apple key
    /// can push to half its devices — and that half-configured install is the likely one, not the exotic one.</para>
    ///
    /// <para>⚠️ <b>This is the <i>Kind</i> half only, and the split is the whole point.</b> Whether credentials are
    /// present is configuration, and answering that here would make this the first capability an operator setting
    /// can flip — exactly the <c>httpsConfigured</c> shape the class note above says every capability avoids. The
    /// <c>AND</c> lives in <c>IOsPushAvailability</c>, so <see cref="DeploymentKind.SelfHostedLan"/> stays ✗
    /// <i>whatever</i> is configured: it has no store-distributed app to register a device, and a clinic PC on a
    /// LAN has no egress guarantee to reach FCM or APNs with.</para>
    ///
    /// <para>A method rather than a property on purpose: the <c>bool</c> properties above are the capability
    /// matrix <c>DeploymentProfileTests</c> reflects over and asserts equal to the old <c>IsLocalMode</c> truth
    /// table, and this answer is <c>false</c> for Local — it belongs beside them, not among them.</para>
    /// </summary>
    public bool PermitsOsPush(DevicePlatform platform) => Kind switch
    {
        DeploymentKind.SelfHostedLan => false,
        DeploymentKind.HostedMultiTenant => true,
        DeploymentKind.CloudBrowser => true,
        _ => throw new ArgumentOutOfRangeException(nameof(platform), Kind, "Unhandled deployment kind.")
    };

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
            exposesMetaOnboarding: false,
            allowsSelfRegistration: true,
            // One clinic per install too, so there is no clinic #2 for a public door to create; first-run
            // `setup` (loopback-gated, once) is how the one clinic comes into being.
            allowsPublicClinicSignup: false,
            // No portfolio to run — one cabinet per install — and the vendor is not on that surgery's network.
            servesPlatformConsole: false,
            // The data is on the practice's own PC. Refusing writes there would hold their patient records
            // hostage on hardware we neither own nor host.
            requiresSubscription: false,
            // One clinic per database on hardware nobody else administers: an in-app dump is the only backup
            // this topology can have, and it is the topology the whole feature was written for.
            backsUpItsOwnData: true,
            // The practice owns the machine, the Meta account and the bill. There is no vendor credit line here
            // to meter, and metering their own spend from their own PC is not a service.
            sellsVendorMessaging: false,
            // An admin locked out here has nobody to call: every way back this feature ships needs a second
            // admin or the vendor, and a single-dentist LAN install has neither.
            requiresAdminSecondFactor: false),

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
            exposesMetaOnboarding: true,
            // The only capability where HostedMultiTenant differs from SelfHostedLan while sharing its login
            // provider: an operator provisions the clinic and its admin creates the staff (US-3).
            allowsSelfRegistration: false,
            // The one profile this door exists for: many clinics, our own accounts, and no operator standing by
            // to run `provision-clinic` for each arrival.
            allowsPublicClinicSignup: true,
            // The one topology with a portfolio to administer: many cabinets, one backend, one vendor behind it.
            servesPlatformConsole: true,
            // The only topology we host and bill: 30 free days, then read-only until a payment is recorded.
            requiresSubscription: true,
            // The `backup` sidecar already dumps this deployment off-server on a schedule — and one database
            // holds every cabinet, so an in-app `pg_dump` would hand one practice all the others.
            backsUpItsOwnData: false,
            // The one topology we host and bill, so the one where the vendor's own WhatsApp credit line is what
            // the cabinets' reminders are spent from.
            sellsVendorMessaging: true,
            // Reached over the internet, holding every cabinet's records, with a vendor on call: the one
            // topology where a stolen admin password is the whole attack and a way back genuinely exists.
            requiresAdminSecondFactor: true),

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
            exposesMetaOnboarding: true,
            allowsSelfRegistration: false,
            // Multi-clinic, but Auth0 issues its identities: a signup here would mint a password-backed local
            // account that this profile's login path cannot authenticate.
            allowsPublicClinicSignup: false,
            // Multi-clinic, but Auth0 owns the identities and these clinics are not on the arrangement the
            // console administers.
            servesPlatformConsole: false,
            // Predates the arrangement; its clinics are not on it.
            requiresSubscription: false,
            // Same hosted infrastructure and the same shared database as above: the sidecar backs it up, and an
            // in-app dump would cross tenants.
            backsUpItsOwnData: false,
            // Predates the arrangement: these clinics supply their own WhatsApp credentials through the manual
            // fields the feature closes on the kind above.
            sellsVendorMessaging: false,
            // Auth0 issues these identities and performs the login: a second factor belongs in that tenant's
            // own policy, not bolted on here over a password this product never checks.
            requiresAdminSecondFactor: false),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled deployment kind.")
    };
}
