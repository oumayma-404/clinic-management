using ClinicManagement.Application;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.API.Hubs;
using ClinicManagement.API.Maintenance;
using ClinicManagement.API.Middleware;
using ClinicManagement.API.Startup;
using Microsoft.AspNetCore.HttpOverrides;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Security;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Authorization.Handlers;
using Microsoft.EntityFrameworkCore;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Dashboard;
using Serilog;
using Serilog.Events;

// Offline admin lockout recovery (FR-B6): a one-shot console command that runs on the server PC
// instead of starting the web server. Usage:
//   dotnet run --project ClinicManagement.API -- reset-admin-password [admin-email]
if (args.Length > 0 && string.Equals(args[0], AdminPasswordResetCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await AdminPasswordResetCommand.RunAsync(args);
}

// Create clinic #N and its first administrator, printing a one-time password (multi-tenant-cloud US-3). The HTTP
// equivalent, POST /api/auth/setup, is loopback-gated (right for a clinic's own PC, impossible over the internet)
// AND a one-time bootstrap, so it can create an install's first clinic and never its second. Usage:
//   ClinicManagement.API.exe provision-clinic --name <nom> --admin-email <email> --admin-name <nom complet>
// Hosted: docker exec clinic-api-prod dotnet ClinicManagement.API.dll provision-clinic …
if (args.Length > 0 && string.Equals(args[0], ProvisionClinicCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await ProvisionClinicCommand.RunAsync(args);
}

// Idempotent HTTPS-cert provisioning (Server Installer Reliability): a one-shot console command that
// generates (or reuses) the CA + server cert into .local/ and exits, without starting the web server or
// touching the DB. The installer runs this BEFORE starting the API service so the service's first boot
// reuses the cert instead of generating it under the ~30s SCM start timeout. Usage:
//   ClinicManagement.API.exe provision-cert
// Create / deactivate / re-secret a vendor CONSOLE account (platform-console AC-8.1/8.2/8.5). There is
// deliberately no web path to any of the three: the account it mints can read every cabinet in the deployment.
// Gated on a configured connection string, NOT on ServesPlatformConsole — the first account has to be creatable
// before the listener is switched on. Usage:
//   ClinicManagement.API.exe platform-account create --email … --name …
if (args.Length > 0 && string.Equals(args[0], PlatformAccountCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await PlatformAccountCommand.RunAsync(args);
}

// Runs the console's activity counter pass once, now, rather than at its 03:00 schedule. It calls the job itself,
// so there is no second copy of the counter rules. Usage:
//   ClinicManagement.API.exe count-activity
if (args.Length > 0 && string.Equals(args[0], CountActivityCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await CountActivityCommand.RunAsync();
}

if (args.Length > 0 && string.Equals(args[0], ProvisionCertCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return ProvisionCertCommand.Run(args);
}

// Money reconciliation (Data & Money Integrity, slice H): a read-only, cross-clinic report of every figure a
// data migration must not move. Runs without starting the web server so it can be used on a stopped app —
// run it before a migration, keep the output, run it after, and diff. Usage:
//   ClinicManagement.API.exe reconcile-money [months-of-history]
// Exit codes: 0 = clean, 1 = could not run, 2 = ran and found drift.
if (args.Length > 0 && string.Equals(args[0], ReconcileMoneyCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await ReconcileMoneyCommand.RunAsync(args);
}

// Schema verification (audit §§ 3–10, plan Testing Strategy): asserts the database actually has the schema the
// EF model describes — indexes, foreign keys, decimal precision — plus the exclusion constraint's partiality,
// btree_gist, and the row counts that prove each data migration covered its rows. Nothing in the test project
// touches a database, so this is the ONLY gate for a schema-level change. Same before/after-and-diff workflow
// as reconcile-money, and read-only. Usage:
//   ClinicManagement.API.exe verify-schema
// Exit codes: 0 = matches the model, 1 = could not run, 2 = ran and found drift.
if (args.Length > 0 && string.Equals(args[0], VerifySchemaCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await VerifySchemaCommand.RunAsync(args);
}

// Reset one clinic account's second factor (hosted-security-hardening FR-1.4) — the third way back, for the
// cabinet where nobody on site can act: no recovery code left and no second administrator. ⚠️ Without a branch
// here the verb would boot the WEB HOST instead and read to an operator as « the command did nothing », which is
// the trap SubscriptionVendorCommandReachabilityTests exists to catch.
if (args.Length > 0 && string.Equals(args[0], ResetUserTotpConsoleCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await ResetUserTotpConsoleCommand.RunAsync(args);
}

// Re-encrypt every stored secret under the key ring's current generation (hosted-security-hardening FR-3.1).
// Configuring certificate protection encrypts keys the ring WRITES from then on and re-wraps nothing already on
// the volume, so without this the master key stays readable off a stolen disk while FR-3.1 reads satisfied.
// Run it, confirm `verify-schema`'s secrets-protected-under-current-ring reads zero, and only THEN delete the
// superseded plaintext key files — the reverse order is R-2's data loss. Usage:
//   ClinicManagement.API.exe reprotect-secrets [--rotate]
// Exit codes: 0 = every secret current, 1 = could not run, 2 = ran and work remains.
if (args.Length > 0 && string.Equals(args[0], ReprotectSecretsCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await ReprotectSecretsCommand.RunAsync(args);
}

// Restore a backup (L4g). A restore runs with the application STOPPED — it drops and recreates every table the
// app holds open — so an endpoint inside the app being replaced is the wrong shape; this is the fourth verb of the
// same family. It validates the folder, refuses while the app is listening, takes a safety dump of the current
// state, restores with `pg_restore --clean --if-exists`, copies `files/` back and invalidates every live session.
// Every refusal happens BEFORE anything is destroyed. Usage:
//   ClinicManagement.API.exe restore-backup <dossier> [--force]
// Exit codes: 0 = restored, 1 = refused or failed.
if (args.Length > 0 && string.Equals(args[0], RestoreBackupCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await RestoreBackupCommand.RunAsync(args);
}

// The vendor's entitlement verbs (clinic-subscription Part F, US-5 / FR-6). Records a received payment, corrects a
// mis-keyed one, suspends for abuse, lifts a suspension, and reports where every cabinet stands. Console verbs and
// NOT endpoints, deliberately: a cabinet able to extend its own entitlement over HTTP would not have one — so no
// controller anywhere references the three commands behind these. All five are gated on the connection string
// (amendment M3), never on a deployment capability: they run no PostgreSQL binary, and the hosted deployment they
// exist for above all has no local DB tooling. Usage:
//   ClinicManagement.API.exe subscription-grant --clinic <id|email> --months 12 [--plan …] [--amount …] [--method …]
//   ClinicManagement.API.exe subscription-cancel --clinic <id|email> --entry <id> --reason "<motif>"
//   ClinicManagement.API.exe subscription-suspend --clinic <id|email> --reason "<motif>"
//   ClinicManagement.API.exe subscription-unsuspend --clinic <id|email>
//   ClinicManagement.API.exe subscription-report [--within 7] [--clinic <id|email>]
// The report shares reconcile-money's exit codes: 0 = nothing to do, 1 = could not run, 2 = cabinets found.
if (args.Length > 0 && string.Equals(args[0], SubscriptionGrantCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await SubscriptionGrantCommand.RunAsync(args);
}

if (args.Length > 0 && string.Equals(args[0], SubscriptionCancelCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await SubscriptionCancelCommand.RunAsync(args);
}

if (args.Length > 0 && string.Equals(args[0], SubscriptionSuspendCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await SubscriptionSuspendCommand.RunAsync(args);
}

if (args.Length > 0 && string.Equals(args[0], SubscriptionUnsuspendCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await SubscriptionUnsuspendCommand.RunAsync(args);
}

if (args.Length > 0 && string.Equals(args[0], SubscriptionReportCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await SubscriptionReportCommand.RunAsync(args);
}

// The vendor's WhatsApp-forfait verbs (vendor-whatsapp-messaging-quota Part 3, US-9). Records a cabinet's reminder
// allowance, corrects a mis-keyed one, and reports where every cabinet stands. Verbs and NOT endpoints for the reason
// the five above are: a practice able to raise its own forfait would not have one, so no controller anywhere references
// the two commands behind these (AC-9.3). Gated on the connection string like their siblings — they run no PostgreSQL
// binary, and the hosted deployment they exist for has no local DB tooling. Usage:
//   ClinicManagement.API.exe messaging-grant  --clinic <id|email> (--per-month N | --top-up N --month AAAA-MM)
//                                             [--amount …] [--method …] [--reference …] [--note …]
//   ClinicManagement.API.exe messaging-cancel --clinic <id|email> --entry <id> --reason "<motif>"
//   ClinicManagement.API.exe messaging-report [--clinic <id|email>] [--month AAAA-MM]
// The report shares reconcile-money's exit codes: 0 = nothing to do, 1 = could not run, 2 = findings. Its --month is
// what lets it answer for a CLOSED month, which is when the vendor reconciles against Meta's bill.
if (args.Length > 0 && string.Equals(args[0], MessagingGrantCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await MessagingGrantCommand.RunAsync(args);
}

if (args.Length > 0 && string.Equals(args[0], MessagingCancelCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await MessagingCancelCommand.RunAsync(args);
}

if (args.Length > 0 && string.Equals(args[0], MessagingReportCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return await MessagingReportCommand.RunAsync(args);
}

// Install-time permission hardening (security-hardening, audit § 2 findings 1–3): tightens NTFS ACLs on the
// install's data directories so no other local account can read the patient database, the uploaded files,
// the logs, or the .local/ trust store. The installer calls this instead of running icacls itself, so the
// policy has one testable implementation shared with the one-click backup. Usage:
//   ClinicManagement.API.exe harden-permissions <dir> [<dir> ...]
if (args.Length > 0 && string.Equals(args[0], HardenPermissionsCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return HardenPermissionsCommand.Run(args);
}

// DB-credentials protection (security-hardening, audit § 2 finding 4): encrypts .local/db-credentials at
// rest so a disk-level copy of the install folder yields no PostgreSQL passwords, and decrypts it on a
// reinstall so the installer can authenticate against the existing cluster. Usage:
//   ClinicManagement.API.exe protect-credentials
//   ClinicManagement.API.exe read-credentials --out <file>
if (args.Length > 0 && string.Equals(args[0], CredentialProtectionCommand.ProtectCommandName, StringComparison.OrdinalIgnoreCase))
{
    return CredentialProtectionCommand.RunProtect(args);
}

if (args.Length > 0 && string.Equals(args[0], CredentialProtectionCommand.ReadCommandName, StringComparison.OrdinalIgnoreCase))
{
    return CredentialProtectionCommand.RunRead(args);
}

// Resolve the deployment profile early (before Serilog is configured) so an install that runs as a Windows
// service can anchor the log file to the install directory (R-6) — a service's CWD is System32, where a
// relative "logs/" path would scatter or fail. Cloud keeps its prior relative path, byte-for-byte. This early
// config is also the seam used for the outer-catch startup-failure handling below (both need the profile
// before builder.Build()). An unrecognised Deployment:Profile throws here, i.e. before anything binds.
// L4e — the layers come from InstallConfiguration so the host and the four console verbs cannot read a
// different set. `appsettings.Install.json` (installer-owned, machine-derived) sits between the shipped defaults
// and the operator's own `appsettings.Production.json`, which is what stopped an upgrade from truncating it.
var startupConfig = new ConfigurationBuilder().AddInstallLayers().Build();
var startupProfile = DeploymentProfile.Resolve(startupConfig);
var logFilePath = startupProfile.RunsAsWindowsService
    ? Path.Combine(LocalInstallPaths.BaseDirectory, "logs", "clinic-management-.log")
    : "logs/clinic-management-.log";

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning) // Hide DB queries
    .MinimumLevel.Override("Hangfire", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: logFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Shared JWT event wiring so the SignalR hub authenticates in BOTH auth modes. A browser WebSocket
// handshake cannot set an Authorization header, so the SignalR client passes the JWT as the
// `access_token` query param; pull it into the token for hub paths only. Same-signed token, same
// validation as the REST API — this only changes WHERE the token is read from for /hub requests.
//
// SECURITY (feature-review Finding 1 — bearer token in the query string): because the token rides in
// the query string, any request logging that records full URLs would capture it. This is intentionally
// safe today — framework request logging is suppressed (Microsoft.AspNetCore → Warning above) and no
// UseSerilogRequestLogging / reverse-proxy access log is wired. If HTTP request logging is ever enabled
// (Serilog request logging, a YARP/front-door access log, etc.), it MUST scrub or omit the query string
// for `/hub/*`, or those logs must be treated as secret-bearing. Do not log `/hub` request URLs verbatim.
static Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents CreateHubJwtEvents() => new()
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(accessToken) &&
            context.HttpContext.Request.Path.StartsWithSegments("/hub"))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
};

try
{
    Log.Information("Starting Clinic Management API");

    var builder = WebApplication.CreateBuilder(args);

    // L4e — the host reads the same layers the early config and the console verbs do. `CreateBuilder` already
    // adds `appsettings.json` + `appsettings.{Environment}.json`; adding them again through `AddInstallLayers`
    // is harmless (identical values, later wins) and is what inserts the installer-owned
    // `appsettings.Install.json` beneath the operator's `appsettings.Production.json`. Environment variables are
    // re-added last so they keep outranking every file, exactly as before.
    builder.Configuration.AddInstallLayers();

    // Use Serilog for logging
    builder.Host.UseSerilog();

    // Local installs (Phase 5 S2): run as an auto-starting Windows service. UseWindowsService() also sets
    // the content root to the install directory. Gated on the capability so hosted profiles are unaffected;
    // it is additionally a no-op when the process was not launched as a Windows service.
    if (startupProfile.RunsAsWindowsService)
    {
        builder.Host.UseWindowsService();
    }

    // Add services to the container
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });

    // Model-binding failures answer in the product's OWN error contract, in French.
    //
    // ⚠️ Without this, ASP.NET returns its RFC 9110 ProblemDetails — `{ title, status, errors: { Field: [...] } }`
    // — whose `title` is the English « One or more validation errors occurred. » and whose details are English
    // machine text naming a C# property. `web/lib/api/client.ts` reads `{ error }` and deliberately discards a
    // detail that `looksTechnical`, so a binding refusal reached the user as **nothing at all**: the dialog simply
    // did not save. Found on « Ajouter un patient », where a non-nullable `string PhoneNumber` made the binder
    // require a field the form never marked required — two defects that only added up to silence.
    // ⚠️ The field NAMES are kept (camelCased, so they match the JSON the client sent) because « which field »
    // is the one thing the user needs and the one thing a generic sentence cannot carry. The framework's English
    // explanations are dropped: they describe a type system, not a form.
    builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var fields = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .Select(entry => entry.Key.Split('.').Last())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => char.ToLowerInvariant(name[0]) + name[1..])
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var message = fields.Count switch
            {
                0 => "Les données envoyées ne sont pas valides.",
                1 => $"Le champ « {fields[0]} » n'est pas valide ou n'a pas été envoyé.",
                _ => $"Ces champs ne sont pas valides ou n'ont pas été envoyés : {string.Join(", ", fields)}."
            };

            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { error = message, code = "validation_failed" });
        };
    });
    // Rate limiting (security-hardening US-4): per-IP on the anonymous auth endpoints, generous per-user
    // elsewhere, with the connectivity poll / OAuth callback / SignalR hub / proxied Next traffic exempt.
    // Both auth modes — Cloud is internet-facing and needs it at least as much as a LAN install.
    builder.Services.AddConfiguredRateLimiter(builder.Configuration);

    // Liveness for whoever decides whether this instance may take traffic (multi-tenant-cloud US-6). Every
    // profile: a datacentre orchestrator polls it, and on a clinic's PC it gives the installer's smoke test
    // something to check that is not a login. See HealthChecks.
    builder.Services.AddConfiguredHealthChecks();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Clinic Management API",
            Version = "v1"
        });
        
        // Map IFormFile to binary schema to prevent Swashbuckle errors
        c.MapType<Microsoft.AspNetCore.Http.IFormFile>(() => new Microsoft.OpenApi.Models.OpenApiSchema
        {
            Type = "string",
            Format = "binary"
        });
        
        // Configure file upload support for IFormFile
        // Order matters: parameter filter runs first, then operation filter
        c.ParameterFilter<ClinicManagement.API.Swagger.FileUploadParameterFilter>();
        c.OperationFilter<ClinicManagement.API.Swagger.FileUploadOperationFilter>();
    });

    // JWT Authentication — profile-branched. Auth0-issued tokens when the deployment defers identity to Auth0;
    // app-issued tokens signed with the per-install key when it owns its own accounts. Authorization policies
    // are the same either way.
    // Resolved from builder.Configuration rather than reused from startupProfile: CreateBuilder(args) adds
    // command-line arguments, so this is the host's authoritative view of the same key.
    var profile = DeploymentProfile.Resolve(builder.Configuration);
    // The vendor console (platform-console). TWO questions, deliberately separate: may it exist here at all
    // (the capability, derived from the deployment kind), and is it bound (the port, an operator setting). Off
    // means ABSENT — no listener, no reachable route, 404 — never present-and-refusing (AC-1.8).
    var consolePort = profile.ServesPlatformConsole
        ? ClinicManagement.Infrastructure.Auth.PlatformAuthConfig.Port(builder.Configuration)
        : 0;

    var auth0Domain = builder.Configuration["Auth0:Domain"];
    var auth0Audience = builder.Configuration["Auth0:Audience"];

    var authConfigured = false;

    if (profile.UsesLocalAccounts)
    {
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = ClinicManagement.Infrastructure.Auth.LocalAuthConfig.Issuer(builder.Configuration),
                ValidateAudience = true,
                ValidAudience = ClinicManagement.Infrastructure.Auth.LocalAuthConfig.Audience(builder.Configuration),
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = ClinicManagement.Infrastructure.Auth.LocalAuthConfig.SecurityKey(builder.Configuration),
                ClockSkew = TimeSpan.Zero
            };
            options.Events = CreateHubJwtEvents();
        });
        authConfigured = true;
    }
    else if (!string.IsNullOrEmpty(auth0Domain) && !string.IsNullOrEmpty(auth0Audience))
    {
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://{auth0Domain}";
            options.Audience = auth0Audience;
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero
            };
            options.Events = CreateHubJwtEvents();
        });
        authConfigured = true;
    }

    // The console's own bearer scheme, added to the SAME authentication builder as the clinic's. Its issuer,
    // audience and signing key are all distinct (PlatformAuthConfig), which is what makes AC-1.4 true by
    // construction: each scheme fails the other's validation, so a token presented to the wrong surface is
    // refused as UNAUTHENTICATED rather than merely unauthorised.
    // ⚠️ Registered only when the console is actually bound. Where it is not, ConsolePortGate 404s every console
    // route before authentication runs, so the scheme is unreachable and its absence costs nothing — while
    // requiring Console:SigningKey on every deployment would break two profiles that have no console.
    if (authConfigured && consolePort > 0)
    {
        // Throws with an operator sentence when Console:SigningKey is absent or too short. Loud on purpose: a
        // console bound with no key of its own would have to borrow the clinic's, and the isolation above is the
        // entire security property of this surface.
        var consoleSigningKey = ClinicManagement.Infrastructure.Auth.PlatformAuthConfig
            .SecurityKey(builder.Configuration);

        builder.Services
            .AddAuthentication()
            .AddJwtBearer(PlatformConsoleScheme.Name, options =>
            {
                options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = ClinicManagement.Infrastructure.Auth.PlatformAuthConfig
                        .Issuer(builder.Configuration),
                    ValidateAudience = true,
                    ValidAudience = ClinicManagement.Infrastructure.Auth.PlatformAuthConfig
                        .Audience(builder.Configuration),
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = consoleSigningKey,
                    ClockSkew = TimeSpan.Zero
                };
            });
    }

    if (authConfigured)
    {
        builder.Services.AddAuthorization(options =>
        {
            // FR-E3 release gate: install a fail-closed fallback policy so every endpoint without an explicit
            // [AllowAnonymous] requires an authenticated session. ConfigurePolicies keeps its bool parameter
            // because it lives in Application, which cannot reference Infrastructure.
            AuthorizationPolicies.ConfigurePolicies(options, profile.FailClosedAuthz);
        });

        // Register authorization handlers
        builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, ClinicManagement.Application.Common.Authorization.Handlers.RoleAuthorizationHandler>();
    }

    // Register HttpContextAccessor (required for ClinicContext)
    builder.Services.AddHttpContextAccessor();

    // Add Application layer
    builder.Services.AddApplication();

    // Add Infrastructure layer
    builder.Services.AddInfrastructure(builder.Configuration);

    // Real-time (SignalR): the clinic-scoped hub + the outbound notifier that appointment handlers use
    // to broadcast "appointments changed" to a clinic's connected clients. Runs in both auth modes.
    builder.Services.AddSignalR();
    builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

    // Add Hangfire for background jobs
    var hangfireConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    // Secrets are no longer committed to appsettings.json (feature cloud-security-and-tenant-isolation,
    // AC-3): the DB connection string must be supplied out-of-band — Cloud via the environment
    // (ConnectionStrings__DefaultConnection), the Local installer via appsettings.Production.json, or dev
    // via appsettings.Development.json. Fail loud rather than starting with no database.
    if (string.IsNullOrWhiteSpace(hangfireConnectionString))
    {
        Log.Fatal("No database connection string configured. Set ConnectionStrings__DefaultConnection " +
                  "(environment) or provide it in the environment's appsettings file. The API cannot start " +
                  "without a database.");
        return 1;
    }

    // Transit (hosted-security-hardening Part 2, FR-2.5): on either HOSTED kind, refuse to start unless the
    // database hop is verified-TLS and the object-store hop is TLS. Placed here, immediately after the
    // connection string is known and BEFORE Hangfire is handed it — Hangfire opens its own connection from the
    // same string, so a check after this line would leave one cleartext consumer behind the guard.
    var transitAssurance = TransportAssurance.Inspect(builder.Configuration, profile, DateTime.UtcNow);
    if (transitAssurance.Applies && !transitAssurance.IsSatisfied)
    {
        StartupDiagnostics.ReportFatal(TransportAssurance.RefusalMessage(transitAssurance));
        return 1;
    }

    // ⚠️ An accepted reduction has to be LOUD, every boot, or it becomes the deployment's forgotten default —
    // which is the failure shape Security:EnforceCsp was left in for a whole release. Warning level, naming the
    // key, so it is visible in Render's log stream and in `docker logs` without anyone going looking.
    if (transitAssurance.Applies && TransportAssurance.AllowsUnverifiedTls(builder.Configuration))
    {
        Log.Warning(
            "{Key} est activé : la connexion à la base de données est CHIFFRÉE mais son identité n'est PAS "
            + "vérifiée (SSL Mode=Require). Un imposteur entre cette application et la base ne serait pas "
            + "détecté. Acceptable seulement sur un hébergeur qui ne publie aucune autorité de certification et "
            + "ne permet pas de monter de fichier ; à retirer dès que l'un des deux devient possible — voir "
            + "follow-up/render-free-tier-transit-relaxation.md.",
            TransportAssurance.AllowUnverifiedTlsKey);
    }

    // Residency: transit asks « is this hop encrypted? »; this asks « where does the data END UP? ». Both are
    // startup refusals because both are decisions an operator makes once and then cannot see — and the second is
    // a LEGAL decision (loi 2004-63 art. 51-52), carried by the clinic rather than by us.
    var residencyAssurance = DataResidencyAssurance.Inspect(builder.Configuration, profile);
    if (residencyAssurance.Applies && !residencyAssurance.IsSatisfied)
    {
        StartupDiagnostics.ReportFatal(DataResidencyAssurance.RefusalMessage(residencyAssurance));
        return 1;
    }

    // ⚠️ Not declaring the allow-list is not an error — it is an undecided deployment, and refusing those would
    // have taught every operator to leave the key empty. But it must not be silent either, so it warns on every
    // boot exactly as the transit relaxation above does.
    if (!residencyAssurance.Applies && !profile.SelfHostsFrontDoor)
    {
        Log.Warning(
            "{Key} n'est pas renseigné : la destination des données de ce déploiement n'est pas vérifiée. "
            + "Tout transfert de données de santé hors de Tunisie exige l'autorisation préalable de l'INPDP "
            + "(loi organique 2004-63, art. 51-52), et la responsabilité en incombe au cabinet. Déclarez les "
            + "hôtes autorisés — voir deploy/README.md, section « Résidence des données ».",
            DataResidencyAssurance.AllowedEgressHostsKey);
    }

    // ⚠️ A destination this process CANNOT check is reported rather than passed over: the nightly backup names an
    // rclone remote whose host lives in a file another container owns, and « unknown » must never read as
    // « checked » on the one question whose wrong answer is a criminal exposure for the practice.
    foreach (var note in residencyAssurance.Unverified)
    {
        Log.Warning("Résidence des données — non vérifiable depuis l'application : {Note}", note);
    }

    // Evidence (hosted-security-hardening Part 4, FR-4.1): resolve the audit chain's key now, so a deployment
    // that cannot chain its ledger refuses to start with the setting named — rather than booting and failing on
    // whichever clinical save happens to be first, where the message reaches nobody who can act on it.
    //
    // ⚠️ Here and not inside `AddInfrastructure`: that method is also called by the console verbs and by test
    // fixtures, so throwing while the container is being built would surface as an unrelated resolution error
    // instead of this sentence. The provider caches, so this resolution is also the one every save later uses.
    try
    {
        _ = new ClinicManagement.Infrastructure.Security.AuditChainKeyProvider(builder.Configuration, profile).Key;
    }
    catch (InvalidOperationException ex)
    {
        StartupDiagnostics.ReportFatal(ex.Message);
        return 1;
    }

    builder.Services.AddHangfire(config =>
        config.UsePostgreSqlStorage(hangfireConnectionString));
    builder.Services.AddHangfireServer();

    // A Windows service is killed by the SCM if it does not report "running" within its ~30s start timeout.
    // Applying the migrations synchronously on a fresh DB (see the migrate block below) exceeded that on first
    // boot and the service was killed before Kestrel bound. Where that applies, defer migrations to a
    // post-startup hosted service so the host reports "started" as soon as it binds; every other profile keeps
    // the synchronous migrate (no service-start timeout to stay inside).
    if (profile.DefersMigrations)
    {
        builder.Services.AddHostedService<ClinicManagement.API.Startup.DeferredStartupService>();
    }

    // Add CORS
    // Note: When using credentials (cookies), we cannot use AllowAnyOrigin()
    // We must specify the exact origin(s) and use AllowCredentials().
    // The origin list is FrontendUrl unioned with the optional Cors:AllowedOrigins array (FR-E1) —
    // so a Local/LAN install can allow client-PC origins via config; Cloud keeps the single FrontendUrl.
    var corsOrigins = ClinicManagement.Infrastructure.CorsOrigins.FromConfiguration(builder.Configuration);
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.WithOrigins(corsOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials(); // Required when sending credentials (cookies)
        });
    });

    // --- Same-origin front door (Local mode, Phase 5 S4) ---
    // Kestrel is the single browser-facing HTTPS endpoint: it serves /api/* via controllers in-process and
    // reverse-proxies every other route (pages, /_next/*, static assets, /bff/*) to the co-located Next
    // server on http://localhost:<webPort>. So one hosted web build serves clients at any server IP
    // (NEXT_PUBLIC_API_URL=/api, same-origin) and TLS terminates once, inside the audited .NET app. A hosted
    // profile installs no proxy (its front door is Caddy, with separate api/web containers) and is unchanged.
    if (profile.SelfHostsFrontDoor)
    {
        var webPort = builder.Configuration.GetValue<int?>("Hosting:WebPort") ?? 3000;
        var proxyRoutes = new[]
        {
            new Yarp.ReverseProxy.Configuration.RouteConfig
            {
                RouteId = "next-app",
                ClusterId = "next-cluster",
                // Least-specific catch-all: attribute-routed /api/* controllers are more specific and win
                // endpoint selection; every other path falls here and is forwarded to Next.
                Match = new Yarp.ReverseProxy.Configuration.RouteMatch { Path = "/{**catch-all}" }
            }
        };
        var proxyClusters = new[]
        {
            new Yarp.ReverseProxy.Configuration.ClusterConfig
            {
                ClusterId = "next-cluster",
                Destinations = new Dictionary<string, Yarp.ReverseProxy.Configuration.DestinationConfig>
                {
                    ["next"] = new Yarp.ReverseProxy.Configuration.DestinationConfig
                    {
                        Address = $"http://localhost:{webPort}"
                    }
                }
            }
        };
        builder.Services.AddReverseProxy().LoadFromMemory(proxyRoutes, proxyClusters);
    }

    // --- LAN hosting: config-driven bind + HTTPS serving (FR-E2, FR-E4) ---
    // Cert password is sourced from the .local/ store or env, never committed appsettings (S3 / Finding 5).
    var httpsCertPath = builder.Configuration["Https:CertPath"];
    var httpsCertPassword = builder.Configuration["Https:CertPassword"];
    var httpPort = builder.Configuration.GetValue<int?>("Hosting:HttpPort") ?? 5000;
    var httpsPort = builder.Configuration.GetValue<int?>("Hosting:HttpsPort") ?? 5001;
    // LAN device trust (P8, AC-44): a cleartext LAN port serving ONLY the trust page. It has to be cleartext
    // and it has to be LAN-reachable, because a phone that does not trust our certificate yet cannot be asked
    // to fetch the fix over that certificate. 0 switches the feature off.
    var trustPort = builder.Configuration.GetValue<int?>("Hosting:TrustPort")
                    ?? ClinicManagement.API.Startup.TrustPortGate.DefaultPort;
    // "generated" | "configured" | "cloud" — logged in the startup transport posture (S3 step 4).
    var certSource = "cloud";

    if (profile.SelfSignsCertificate)
    {
        // Always serve HTTPS here. If a cert path is explicitly configured it MUST exist — refuse the silent
        // HTTP downgrade (Phase 4 Finding 2 / fail closed & loud). Otherwise self-generate a CA + server cert
        // into .local/ (FR-E2). Finding 4 warned against gating this on `httpsConfigured`, a *configuration*
        // value that merely correlated with the mode; SelfSignsCertificate is derived from the profile itself,
        // so no operator setting can flip it without changing the profile.
        string certPath;
        string? certPassword;

        if (!string.IsNullOrWhiteSpace(httpsCertPath))
        {
            if (!System.IO.File.Exists(httpsCertPath))
            {
                StartupDiagnostics.ReportFatal(
                    $"Https:CertPath est défini ('{httpsCertPath}') mais le fichier est introuvable. Le serveur " +
                    "refuse de démarrer en HTTP non chiffré. Corrigez le chemin du certificat ou retirez Https:CertPath.");
                return 1;
            }
            certPath = httpsCertPath;
            certPassword = httpsCertPassword;
            certSource = "configured";
        }
        else
        {
            // Use a real (Serilog-backed) logger so the provisioner's own generate-vs-reuse log lines are
            // visible (Finding 17); it runs pre-Build, before the DI container/ILoggerFactory exists.
            var provisioner = new CertificateProvisioner(
                new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger)
                    .CreateLogger<CertificateProvisioner>());
            var generated = provisioner.EnsureServerCertificate();
            certPath = generated.PfxPath;
            certPassword = generated.Password;
            certSource = "generated";
            Log.Information("Local HTTPS certificate ready; CA exported to {CaCertPath} for client trust import.", generated.CaCertPath);
        }

        // A trust port that collides with a real port would make TrustPortGate 404 the entire application on
        // that port — the app would start and then answer nothing. Refuse at startup instead of shipping the
        // outage (same fail-loud posture as a missing Https:CertPath above).
        var proxiedWebPort = builder.Configuration.GetValue<int?>("Hosting:WebPort") ?? 3000;
        if (trustPort > 0
            && (trustPort == httpPort || trustPort == httpsPort || trustPort == proxiedWebPort))
        {
            StartupDiagnostics.ReportFatal(
                $"Hosting:TrustPort ({trustPort}) doit être un port distinct de Hosting:HttpPort ({httpPort}), " +
                $"Hosting:HttpsPort ({httpsPort}) et Hosting:WebPort ({proxiedWebPort}). " +
                "Choisissez un port libre, ou mettez Hosting:TrustPort à 0 pour désactiver la page de confiance.");
            return 1;
        }

        var serverCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath, certPassword);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Plain HTTP stays LOOPBACK-only (Finding 2): the sole legitimate consumer is the co-located
            // Next BFF over http://localhost:5000. Binding it on every LAN interface would expose the
            // cleartext API (incl. POST /api/auth/login) if the firewall rule is removed/disabled. The
            // LAN-facing surface is the HTTPS front door only.
            kestrel.ListenLocalhost(httpPort);
            kestrel.ListenAnyIP(httpsPort, listen => listen.UseHttps(serverCertificate));

            // ⚠️ A SEPARATE cleartext LAN port for the trust page — deliberately NOT a widening of 5000.
            // Read this together with TrustPortGate: a Kestrel listener is not scoped to a subset of routes,
            // so this bind alone would publish EVERY endpoint in cleartext on the LAN, which is the exact
            // exposure the loopback bind above exists to prevent. The gate middleware refuses everything
            // except /api/trust on this port, and that is what keeps the two consistent.
            if (trustPort > 0)
            {
                kestrel.ListenAnyIP(trustPort);
            }
        });
        builder.Services.AddHttpsRedirection(options => options.HttpsPort = httpsPort);
    }
    else
    {
        // HOSTED — byte-for-byte unchanged: opt-in HTTPS only when a cert file exists, else honor Hosting:Urls
        // (behind Caddy, TLS terminates at the proxy and this stays plain HTTP on the container network).
        var httpsConfigured = !string.IsNullOrWhiteSpace(httpsCertPath) && System.IO.File.Exists(httpsCertPath);
        if (httpsConfigured)
        {
            var serverCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                httpsCertPath!, httpsCertPassword);
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.ListenAnyIP(httpPort);
                kestrel.ListenAnyIP(httpsPort, listen => listen.UseHttps(serverCertificate));
            });
            builder.Services.AddHttpsRedirection(options => options.HttpsPort = httpsPort);
        }
        else
        {
            // ⚠️ THE CONSOLE LISTENER CANNOT BE ADDED AS ONE MORE LINE HERE (platform-console risk R-3a).
            // In HostedMultiTenant there is no certificate file, so this branch runs and — until now — never
            // called ConfigureKestrel at all: the only thing binding port 5000 is ASPNETCORE_URLS from the compose
            // file. Kestrel's explicit endpoints OVERRIDE the URLs configuration wholesale, so a bare
            // ConfigureKestrel(k => k.ListenAnyIP(consolePort)) would unbind 5000, Caddy's /api/* → api:5000 would
            // stop resolving, and the entire product would go dark while the console itself worked perfectly.
            // The failure is one line in Kestrel's log ("Overriding address(es)…") and silent everywhere else.
            // So both ports are resolved together and bound in ONE call — see ConsoleListenerPlanning.
            var listenerPlan = ConsoleListenerPlanning.Resolve(
                builder.Configuration, profile.ServesPlatformConsole, consolePort);

            if (listenerPlan is not null)
            {
                builder.WebHost.ConfigureKestrel(kestrel =>
                {
                    kestrel.ListenAnyIP(listenerPlan.PublicPort);
                    // A Kestrel listener is not scoped to a subset of routes: EVERY endpoint answers on this port
                    // too. ConsolePortGate is what makes it the console's, in both directions.
                    kestrel.ListenAnyIP(listenerPlan.ConsolePort);
                });

                // Answerable from the log rather than from `ss -ltnp` inside a container — and the line an
                // operator checks first when « the API stopped answering after we enabled the console ».
                Log.Information(
                    "Bound the public API on port {PublicPort} and the vendor console on port {ConsolePort}.",
                    listenerPlan.PublicPort, listenerPlan.ConsolePort);
            }
            else
            {
                // Console off ⇒ touch none of this, so CloudBrowser and a console-less hosted deployment keep
                // their current UseUrls/ASPNETCORE_URLS behaviour byte for byte.
                var hostingUrls = builder.Configuration["Hosting:Urls"];
                if (!string.IsNullOrWhiteSpace(hostingUrls))
                {
                    builder.WebHost.UseUrls(hostingUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
            }
        }
    }

    var app = builder.Build();

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // FIRST, before anything can substitute the peer (hosted-security-hardening Part 2, FR-2.4 / R-5). The two
    // loopback-only gates — first-run `setup` and the Hangfire dashboard — read the captured value through
    // LocalRequest, so they stay a property of the real TCP peer rather than of a header's trust bound.
    app.UseOriginalPeerCapture();

    // Honour the reverse proxy's X-Forwarded-For and X-Forwarded-Proto, BOUNDED to the proxy's own address
    // (FR-2.4). Only where the front door is not self-hosted: SelfHostedLan terminates TLS in this process and
    // FR-2.7 says nothing about that path may change.
    //
    // ⚠️ An empty or unparseable Security:TrustedProxies means the headers are IGNORED ENTIRELY and the log says
    // so — never an unbounded header. `ForwardedHeadersOptions` defaults to trusting loopback only, which sounds
    // safe and is useless here: behind a proxy every request arrives from a bridge address, so the middleware
    // would rewrite nothing while looking active. Refusing to register it at all is the honest version of the
    // same outcome, and it leaves ClientIp — which never stopped resolving the address separately — in charge.
    //
    // ⚠️ ForwardLimit stays at its default of 1: exactly one hop in front of the API is ours. A larger limit
    // walks further left along X-Forwarded-For, which is caller-supplied text, so a client could inject an
    // extra entry and choose the address it is attributed to.
    //
    // ⚠️ XForwardedHost is deliberately NOT processed. Request.Host feeds the Google OAuth redirect-uri
    // fall-back, and a forged host there would build a redirect URI pointing somewhere else.
    if (!profile.SelfHostsFrontDoor)
    {
        var forwardedFrom = ClinicManagement.Infrastructure.TrustedProxies.FromConfiguration(builder.Configuration);
        if (forwardedFrom.Networks.Count == 0)
        {
            Log.Warning(
                "Forwarded headers are IGNORED because {Key} {Reason}. Behind a reverse proxy this means every "
                + "request is attributed to the proxy's own address — the rate limiter, the login lockout and "
                + "the OAuth state cookie's Secure flag all see the hop, not the client. Set {Key}__0 to the "
                + "network the proxy reaches this service from.",
                ClinicManagement.Infrastructure.TrustedProxies.ConfigurationKey,
                forwardedFrom.ConfiguredEntryCount == 0
                    ? "is not set"
                    : $"holds {forwardedFrom.ConfiguredEntryCount} entry(ies) and none of them parsed");
        }
        else
        {
            var forwardedOptions = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            };

            // The same parsed set the rate limiter and the login lockout believe, taken from TrustedProxies
            // rather than re-read here: two parsers of one setting is how the header middleware and the
            // limiter end up trusting different hops. The framework's loopback defaults are left in place —
            // the co-located BFF hop is real in every profile, and TrustedProxies trusts loopback too.
            foreach (var network in forwardedFrom.Networks)
            {
                forwardedOptions.KnownNetworks.Add(new IPNetwork(network.Network, network.PrefixLength));
            }

            app.UseForwardedHeaders(forwardedOptions);

            Log.Information(
                "Forwarded headers honoured from {RangeCount} configured range(s) ({Ranges}).",
                forwardedFrom.Networks.Count,
                string.Join(", ", forwardedFrom.Networks.Select(n => $"{n.Network}/{n.PrefixLength}")));
        }
    }

    // ── FR-4.6: the HTTPS redirect is REMOVED, not configured, and this comment is the decision.
    //
    // It was registered on both hosted kinds and did nothing. `UseHttpsRedirection` needs a target port, which
    // `AddHttpsRedirection` supplies only in the two certificate-bearing branches above — neither of which runs
    // in a container, where TLS terminates at Caddy — and `HTTPS_PORT` is set nowhere. With no port it logs
    // « Failed to determine the https port for redirect » once and passes every request through for ever.
    //
    // Configuring it would have been worse than removing it. Behind the proxy every request arrives on plain
    // HTTP by design, and since Part 2 `UseForwardedHeaders` makes `Request.IsHttps` true — so a redirect would
    // either fire on nothing or, if the headers were ever misread, bounce the proxy's own hop into a loop.
    // Caddy already redirects HTTP → HTTPS at the edge, which is the only place a browser is listening.
    //
    // ⚠️ Removing it is the point rather than a tidy-up: a security control that is present and inert is worse
    // than an absent one, because it reads as present — to a reviewer, to an operator, and to whoever next asks
    // « do we redirect? ». `SelfHostedLan` never registered it (the loopback BFF hop would break), so this
    // deletes the only registration that existed and no profile loses a behaviour it actually had.
    app.UseCors("AllowAll");
    
    // Exception handling middleware (must be before authentication/authorization)
    // First in the pipeline so it also covers the proxied Next application in Local mode, where Kestrel is
    // the single browser-facing endpoint (security-hardening US-12 / AC-12.5).
    app.UseMiddleware<ClinicManagement.API.Middleware.SecurityHeadersMiddleware>();

    // LAN device trust (P8, R-11): the cleartext trust port serves the trust page and NOTHING else.
    // Placed here — after the security headers so the page still gets them, before everything else — so a
    // request for any other path on that port dies before rate limiting, authentication, the controllers and
    // the Next proxy have had any chance to answer it. This is the half that makes ListenAnyIP(trustPort)
    // safe; see TrustPortGate for why the bind alone is not.
    if (profile.ExposesTrustEndpoints && trustPort > 0)
    {
        app.Use(async (context, next) =>
        {
            if (ClinicManagement.API.Startup.TrustPortGate.ShouldRefuse(
                    context.Connection.LocalPort, trustPort, context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next();
        });
    }

    // The console's two-way boundary (platform-console FR-2, AC-1.7). Placed here — after the security headers so
    // a refusal still gets them, before everything else — so a console path on the public port, or any other path
    // on the console port, dies before rate limiting, authentication, the controllers and the Next proxy have had
    // any chance to answer it. Registered UNCONDITIONALLY: with the console off, consolePort is 0 and the gate's
    // job is to 404 every /api/platform route everywhere, which is what « absent » means (AC-1.8).
    app.Use(async (context, next) =>
    {
        if (ConsolePortGate.ShouldRefuse(context.Connection.LocalPort, consolePort, context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next();
    });

    // Immediately before the limiter, because after it the partition has already been chosen: lifts the submitted
    // email out of an auth request's body so the tight window is spent per ACCOUNT instead of per address. See
    // AuthAttemptAccount — it can neither refuse a request nor consume the body.
    app.UseAuthAttemptAccountCapture();

    // Before authentication: an unauthenticated flood must be refused as cheaply as possible, and the
    // anonymous auth endpoints (the brute-force surface) are gated on the submitted account, with the client
    // address as a second and looser ceiling.
    app.UseRateLimiter();

    app.UseMiddleware<ExceptionMiddleware>();

    // A shell below the operator's floor is refused here, BEFORE authentication, so its login 426s rather than
    // 401ing — the client reads 401 as « signed out », and AC-33 requires « mettez à jour » instead. Emits the
    // canonical { error } body so ExceptionMiddleware's contract is not shadowed. Every profile: a client too
    // old for the server is too old everywhere.
    app.UseMiddleware<ClinicManagement.API.Middleware.ClientVersionMiddleware>();

    app.UseAuthentication();

    // Is this account still active, and what role does it ACTUALLY hold? Both are read from the caller's own row
    // and both are enforced in every profile — see AccountStateMiddleware. ⚠️ It runs BEFORE UseAuthorization
    // because the role it publishes is what RoleAuthorizationHandler reads; the cost is one account lookup on a
    // request authorization then refuses, and RequestAccount caches it for everything downstream.
    app.UseMiddleware<ClinicManagement.API.Middleware.AccountStateMiddleware>();

    // The console's own live-state check (AC-1.6). It exists because console requests skip AccountStateMiddleware
    // above AND LocalAuthEnforcementMiddleware below — the product's only two per-request readers of account state
    // — so without it a deactivated console account would keep full cross-cabinet access until its token expired.
    // ⚠️ After UseAuthentication, because before it there is no principal to read and the check would silently
    // pass everything; pinned against this file's own source by PlatformAccountStateTests.
    app.UseMiddleware<ClinicManagement.API.Middleware.PlatformAccountStateMiddleware>();

    app.UseAuthorization();

    // A console request reads across every cabinet, so it declares UseSystemWide explicitly. ⚠️ Before
    // TenantScopeMiddleware, which skips console paths: ITenantScope is single-assignment, and a console request
    // that reached a handler Unset would read ZERO ROWS WITH NO ERROR — a portfolio indistinguishable from one
    // where every cabinet is idle (EC-12).
    app.UseMiddleware<ClinicManagement.API.Middleware.PlatformTenantScopeMiddleware>();

    // Whose rows this request may read. Unconditional and in EVERY profile: the global query filters refuse an
    // unset scope, so a request that reached a controller without passing here would read nothing at all. It
    // reuses the account row the middleware above already resolved (see RequestAccount).
    app.UseMiddleware<ClinicManagement.API.Middleware.TenantScopeMiddleware>();

    // Token-version revocation and the pending forced password change — the two things only a self-issued JWT
    // can have, hence the capability gate. The active-account check that used to live here is now unconditional
    // above: « a deactivated account cannot use the API » is not a property of a topology.
    if (profile.EnforcesTokenState)
    {
        app.UseMiddleware<ClinicManagement.API.Middleware.LocalAuthEnforcementMiddleware>();
    }

    // Last before the controllers, and AFTER the block above rather than beside TenantScopeMiddleware: a 402 must
    // never mask a 401 (revoked token) or a 403 must_change_password, or an expired cabinet's deactivated colleague
    // is told the subscription lapsed and a user owing a password change is routed to « Abonnement » instead of to
    // the screen that unblocks them. It needs nothing the earlier position would have given it — the tenant scope is
    // set, the account is cached, and routing has already run. Inert where RequiresSubscription is false.
    app.UseMiddleware<ClinicManagement.API.Middleware.SubscriptionGateMiddleware>();

    app.MapControllers();

    // Anonymous and un-rate-limited, both deliberately — see HealthChecks.Register. Mapped before the YARP
    // catch-all so a self-hosted front door answers it here rather than proxying /health to the Next app.
    HealthChecks.Register(app);

    // Real-time hub. A literal route, so it is more specific than the Local-mode YARP catch-all below
    // and wins endpoint selection (the WebSocket reaches the hub in-process, not the Next proxy).
    app.MapHub<ClinicHub>("/hub/clinic");

    // Hangfire Dashboard — reachable only from the server machine itself (loopback), in every profile.
    //
    // ⚠️ MAPPED as an endpoint with .AllowAnonymous(), not `UseHangfireDashboard`, and that is a FIX rather
    //    than a style choice. `AuthorizationMiddleware` applies the fail-closed `FallbackPolicy`
    //    (RequireAuthenticatedUser) to a request that reaches it carrying no endpoint metadata — which is what
    //    dashboard *middleware* is — so wherever FailClosedAuthz holds, /hangfire answered **401 with
    //    `WWW-Authenticate: Bearer`** to everyone, loopback included, and `HangfireAuthorizationFilter` was
    //    never consulted at all. The dashboard was documented as loopback-reachable in both modes and was in
    //    fact reachable in neither. Found in Part 7 by trying to trigger a recurring job from the box.
    // ⚠️ AllowAnonymous does NOT open it: the loopback filter below is the gate, and it now actually runs.
    //    This is the same reason `MapReverseProxy().AllowAnonymous()` below carries that call — the fallback
    //    policy would otherwise 401 the login page.
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter() }
    }).AllowAnonymous();

    // Same-origin front door: forward every non-/api route to the localhost Next server. The catch-all is the
    // least-specific endpoint, so /api/* controllers and the loopback-gated /hangfire middleware take
    // precedence. A hosted profile maps no proxy (unchanged).
    if (profile.SelfHostsFrontDoor)
    {
        // The proxy serves the Next web app (login/setup pages, static assets, /bff/* auth routes), which
        // handles its OWN authentication. It must be AllowAnonymous — otherwise the Local-mode fail-closed
        // FallbackPolicy (RequireAuthenticatedUser) gates every page request and returns 401 before the user
        // can even reach the login page. Only /api/* controllers stay behind [Authorize].
        app.MapReverseProxy().AllowAnonymous();
    }

    // Ensure database is created (FR-F3: migrations apply automatically on startup), then run the boot-time
    // data backfills. ⚠️ These are TWO questions that used to share one mode boolean: *when*
    // migrations run is an SCM start-timeout concern, while the backfills are data obligations. Under one flag
    // a new profile gets them right only by accident, and this is the block Part B must scope and Part F must
    // wrap in an advisory lock — so "correct by luck" is not good enough.
    // ⚠️ The two capabilities are now genuinely independent (review finding 23). RunsStartupBackfills used to be
    // evaluated INSIDE the !DefersMigrations branch, so for SelfHostedLan it was unreachable — and a future profile
    // declaring `DefersMigrations: true, RunsStartupBackfills: true` would have silently skipped both, which is
    // precisely the "correct only by accident" the split was made to prevent.
    // FR-3.9 — which key-ring generations this deployment can read, for the backup sidecar to stamp beside each
    // dump. Best-effort: an unwritable marker volume must not take a whole deployment's clinics off the air.
    {
        var markerPath = ClinicManagement.API.Startup.KeyRingGenerationMarker.TryWrite(
            app.Services, builder.Configuration, out var markerProblem);
        if (markerPath is not null)
        {
            Log.Information("Marqueur de génération du trousseau écrit dans {Path} (FR-3.9).", markerPath);
        }
        else if (markerProblem is not null)
        {
            Log.Warning(
                "Le marqueur de génération du trousseau n'a pas pu être écrit ({Problem}). Les sauvegardes "
                + "seront estampillées « unknown » et une restauration sera refusée faute de pouvoir vérifier "
                + "la génération (FR-3.9).", markerProblem);
        }
    }

    if (!profile.DefersMigrations || profile.RunsStartupBackfills)
    {
        using var scope = app.Services.CreateScope();

        // This scope has no request behind it, and the backfills below are per-clinic obligations across every
        // clinic — so the query filters have to be told, or the seeder and the admin backfill would each see an
        // empty database and report success (US-2, R-1).
        scope.ServiceProvider.GetRequiredService<ITenantScope>()
            .UseSystemWide("startup migrations and per-clinic backfills");

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Serialised across instances: EF Core takes no lock of its own, so two containers starting together
        // apply the same migrations concurrently and the loser fails part-way. See MigrationLock. The backfills are
        // inside the same lock because they are check-then-insert, i.e. idempotent against themselves but not
        // against a concurrent twin.
        await ClinicManagement.API.Startup.MigrationLock.RunExclusivelyAsync(
            context.Database,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
            async () =>
            {
                if (!profile.DefersMigrations)
                {
                    await context.Database.MigrateAsync();
                }

                if (profile.RunsStartupBackfills)
                {
                    // Backfill per-clinic reference catalogs for any existing clinic missing one (#5). Idempotent —
                    // new clinics are seeded on creation; this covers clinics that predate the per-clinic conversion.
                    var catalogSeeder = scope.ServiceProvider.GetRequiredService<IClinicCatalogSeeder>();
                    await catalogSeeder.SeedAllClinicsAsync();

                    // Give existing clinics an admin (finding: onboarding used to assign only doctor/secretary, so
                    // pre-fix clinics have none). Idempotent — promotes the earliest user only when a clinic has no
                    // active admin. New clinics already get an admin at creation.
                    var adminBackfill = scope.ServiceProvider.GetRequiredService<IClinicAdminBackfill>();
                    await adminBackfill.BackfillAsync();

                    // Encrypt every Google Calendar refresh token still held in the clear (FR-3.4). A startup
                    // pass and not SQL in the migration, because encrypting needs the key ring — see the file.
                    var converted = await ClinicManagement.API.Startup.GoogleTokenProtectionBackfill.RunAsync(
                        context,
                        scope.ServiceProvider.GetRequiredService<IGoogleTokenProtector>(),
                        scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
                    if (converted > 0)
                    {
                        Log.Information(
                            "Chiffrement au repos : {Count} jeton(s) Google Agenda convertis (FR-3.4).", converted);
                    }
                }
            });
    }


    // Schedule background jobs
    // SMS/WhatsApp appointment-reminder dispatcher — minutely, connectivity-gated (see NotificationJob).
    // Sends only when the server has internet; otherwise it no-ops and leaves rows Pending, so it is safe
    // to run unconditionally (it does nothing until a Reminders channel + credentials are configured).
    RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.NotificationJob>(
        "process-notifications",
        job => job.ProcessPendingNotifications(),
        Cron.Minutely);

    // Document-email outbox dispatcher — minutely, connectivity-gated (see DocumentEmailJob). Sends the queued
    // document PDFs only when the server has internet; otherwise no-ops and leaves them queued, which is what
    // makes « Envoyer par email » meaningful on an offline LAN install. Safe to run unconditionally (does
    // nothing until a clinic configures SMTP and queues a send).
    RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.DocumentEmailJob>(
        "dispatch-document-emails",
        job => job.DispatchQueuedEmails(),
        Cron.Minutely);

    // Auto-start a visit once its own slot has begun — minutely, because the resolution the agenda shows is the
    // minute, and deliberately NOT connectivity-gated: it writes a status, so it must work on an offline LAN
    // install (StockExpiryJob's reasoning). Unconditional like the three passes that no-op until there is work:
    // on a clinic with nothing booked right now the read returns an empty set and the tick costs one query.
    RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.AppointmentProgressJob>(
        "start-running-appointments",
        job => job.StartRunningAppointments(),
        Cron.Minutely);

    // Approaching-expiry stock alerts (AC-P4.6) — daily, deliberately NOT connectivity-gated: the alert is
    // in-app, so it has to work on an offline LAN install. An expiry is crossed by the passage of time rather
    // than by a write, so without this scan the notification would never fire for the case it exists for (a
    // box nobody has touched). Runs at 06:00 UTC — before the clinic opens, so the alert is already in the
    // feed when the first person looks at the bell, rather than appearing mid-morning.
    RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.StockExpiryJob>(
        "flag-expiring-stock",
        job => job.FlagExpiringStock(),
        Cron.Daily(6));

    // Unattended backup (L4a) — HOURLY, not daily, and deliberately not connectivity-gated (the output is a
    // local file, so it must work on an offline LAN install — the same reasoning as the expiry scan above).
    //
    // ⚠️ Hourly is what makes the per-clinic hour real: the schedule lives on the clinic
    // (`Clinic.BackupHourLocal`, clinic-local), so one daily cron could honour only one clinic's choice. It also
    // serves the case a fixed 02:00 cron cannot: a clinic PC switched off overnight would simply never be backed
    // up, silently, for ever. The job itself decides whether each clinic is due, and will not back one up twice
    // in its own local day.
    //
    // ⚠️ Registered ONLY where the application backs its own data up (`BacksUpItsOwnData`). On the two hosted kinds
    // the `deploy/` `backup` sidecar already dumps the database and the object store off-server on a schedule, and
    // one database holds every cabinet — so an in-app `pg_dump` there is both weaker than what is running and a
    // cross-tenant read. Left registered it did real harm rather than nothing: with no `pg_dump` in the image it
    // wrote a failure row per clinic per attempt for ever AND raised a « sauvegarde périmée » alert that no action
    // available to the clinic could clear.
    if (profile.BacksUpItsOwnData)
    {
        RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.BackupJob>(
            "run-scheduled-backups",
            job => job.RunScheduledBackups(),
            Cron.Hourly);
    }
    else
    {
        // Defensively drop it, as the two jobs below do: a deployment reprofiled onto hosted infrastructure would
        // otherwise keep an hourly registration pointing at work it must no longer do.
        RecurringJob.RemoveIfExists("run-scheduled-backups");
    }

    // The vendor console's activity counters (platform-console FR-3) — daily, and deliberately NOT
    // connectivity-gated: its output is a database row, exactly like the expiry scan above. Gating it on egress
    // would freeze the counters through an outage and then report that silence as cabinets going dormant.
    //
    // ⚠️ Registered unconditionally rather than behind `ServesPlatformConsole`. The counters are HISTORY: a
    // deployment that switches the console on later would otherwise open it to a portfolio of « jamais mesuré »
    // with nothing to backfill from, since the pass reads a 30-day audit window and cannot reconstruct months it
    // never ran for. The rows are small and nothing else reads them, so the cost of being wrong the other way is
    // one table per deployment.
    //
    // 03:00 UTC = 04:00 in Tunis: after the day it measures has ended everywhere, and long before the vendor
    // opens the console, so « countersAsOf » is this morning rather than the middle of the working day.
    RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.ClinicActivityCounterJob>(
        "count-clinic-activity",
        job => job.CountClinicActivity(),
        Cron.Daily(3));

    // The daily per-clinic recovery point (clinic-recovery-points) — 02:00 UTC = 03:00 in Tunis, before the counter
    // pass above so the two do not contend for the same connection pool at the same instant.
    //
    // ⚠️ Registered UNCONDITIONALLY, unlike BackupJob beside it, and the asymmetry is the whole point: that one runs
    // pg_dump — which takes --dbname and has no tenant predicate — so it is BacksUpItsOwnData-only. This goes through
    // the tenant filter like every CSV export and carries one cabinet's rows, so it is correct on every deployment
    // kind. On SelfHostedLan it is additionally the only *granular, online* recovery there is: the restore-backup verb
    // stops the app and restores the whole database to undo one deleted fiche.
    RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.ClinicRecoveryPointJob>(
        "take-recovery-points",
        job => job.TakeRecoveryPoints(),
        Cron.Daily(2));

    // OS push dispatcher (mobile-native-shells Part 6) — minutely, connectivity-gated, and registered ONLY where
    // the deployment can actually push (AC-51). Unlike its three siblings above, which are safe to register
    // unconditionally because they no-op until configured, this one is registered conditionally on purpose: a
    // recurring job on a clinic's own PC would appear in the Hangfire dashboard for ever, running every minute to
    // discover again that this topology has no store-distributed app to deliver to.
    var pushAvailability = app.Services.GetRequiredService<IOsPushAvailability>();
    if (pushAvailability.IsAvailableAtAll)
    {
        RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.PushDispatchJob>(
            "dispatch-os-push",
            job => job.DispatchQueuedPushes(),
            Cron.Minutely);
    }
    else
    {
        // Defensively drop it: an install that had credentials and lost them (or was reprofiled) would otherwise
        // keep a registration in Hangfire storage pointing at work it must no longer do.
        RecurringJob.RemoveIfExists("dispatch-os-push");
    }

    // Subscription-expiry warnings (clinic-subscription FR-5) — daily, and registered ONLY where a cabinet's right
    // to record work is a dated entitlement (AC-7.1/7.2): on a clinic's own PC and on the Auth0 deployment every
    // entitlement is open-ended, so the pass could only ever loop over cabinets it must not warn. Not
    // connectivity-gated — the warning is in-app. 07:00 UTC, after the expiry scan and before the clinic opens, so
    // the row is already in the feed when the first person looks at the bell.
    if (profile.RequiresSubscription)
    {
        RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.SubscriptionWarningJob>(
            "warn-subscription-expiry",
            job => job.WarnExpiringSubscriptions(),
            Cron.Daily(7));
    }
    else
    {
        // Defensively drop it, as the push dispatcher does: an install reprofiled away from the hosted kind would
        // otherwise keep a registration pointing at work it must no longer do.
        RecurringJob.RemoveIfExists("warn-subscription-expiry");
    }

    // The WhatsApp reminder forfait's daily pass (vendor-whatsapp-messaging-quota D-2) — provision each cabinet's
    // counting row for the current Tunisian month, then reconcile the three warnings. Registered ONLY where the
    // deployment sells vendor messaging (EC-16), on SubscriptionWarningJob's precedent: elsewhere there is no forfait
    // to provision and the pass could only ever loop over cabinets it must not touch. Not connectivity-gated — one
    // duty writes a database row and the other writes the in-app feed. 05:00 UTC = 06:00 Tunis: after the Tunisian
    // month has genuinely turned (so a rollover withdrawal is not racing midnight) and before the clinic opens.
    if (profile.SellsVendorMessaging)
    {
        RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.MessagingAllowanceJob>(
            "review-messaging-allowances",
            job => job.ReviewMessagingAllowances(),
            Cron.Daily(5));
    }
    else
    {
        // Defensively drop it, as the two jobs above do: an install reprofiled away from the hosted kind would
        // otherwise keep a registration pointing at work it must no longer do.
        RecurringJob.RemoveIfExists("review-messaging-allowances");
    }

    // Google→App calendar sync never runs on a schedule: the recurring job and its GoogleCalendarSyncJob
    // class were removed as dead scaffolding. App→Google sync runs inline on appointment create/update, and
    // Google→App stays manual-only (GoogleCalendarController). Defensively drop any stale recurring
    // registration a previous deploy may have left in Hangfire storage so it can't fire a deleted job type.
    RecurringJob.RemoveIfExists("sync-google-calendar");

    // The electronic-invoicing subsystem was removed wholesale (adoption-gaps-remediation Part 1). Drop the
    // registration an upgrading install still has in Hangfire storage, or it fires every minute at a job type
    // that no longer exists. The literal below is that stored job's id, so it cannot be reworded.
    RecurringJob.RemoveIfExists("dispatch-einvoices");

    // Log the transport posture on startup so it is observable (S3 step 4 / fail-loud-and-observable).
    if (profile.SelfSignsCertificate)
    {
        Log.Information(
            "Transport posture ({Profile}): HTTPS on port {HttpsPort} (HTTP {HttpPort} redirects), certificate source: {CertSource}.",
            profile.Kind, httpsPort, httpPort, certSource);
    }

    Log.Information("Clinic Management API started successfully");
    app.Run();
}
catch (Exception ex) when (startupProfile.SelfHostsFrontDoor && StartupDiagnostics.IsAddressInUse(ex))
{
    // FR-F5: the bind port is already taken — name it and exit non-zero with a clear message instead of a raw
    // AddressInUseException. Uses the early startup profile (the in-try one is out of scope here). A hosted
    // profile falls through to the fatal-rethrow below, unchanged.
    var port = startupConfig.GetValue<int?>("Hosting:HttpsPort") ?? 5001;
    StartupDiagnostics.ReportFatal(StartupDiagnostics.PortInUseMessage(port), ex);
    return 1;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Authorization filter for the Hangfire dashboard.
// The dashboard exposes background-job internals and payloads — and in a multi-tenant Cloud deployment
// those payloads are cross-tenant — so it must never be reachable from a LAN client or the public
// internet. In BOTH modes we allow only requests that originate from the server machine itself
// (loopback); operators reach it via RDP / an SSH tunnel on the host. Cloud previously returned `true`
// unconditionally, leaving /hangfire open to anyone who could reach the host (feature
// cloud-security-and-tenant-isolation, AC-2).
// Reverse-proxy caveat: behind a proxy the client IP is the proxy's, not loopback — so /hangfire denies
// even the operator through the proxy (fail-safe); reach it from the host loopback instead. Acceptable
// for the current topology (Cloud host reached directly; Local front door is co-located on the same PC).
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
        => ClinicManagement.Infrastructure.LocalRequest.IsLoopback(context.GetHttpContext());
}
