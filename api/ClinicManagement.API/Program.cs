using ClinicManagement.Application;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.API.Hubs;
using ClinicManagement.API.Maintenance;
using ClinicManagement.API.Startup;
using ClinicManagement.Infrastructure;
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

// Idempotent HTTPS-cert provisioning (Server Installer Reliability): a one-shot console command that
// generates (or reuses) the CA + server cert into .local/ and exits, without starting the web server or
// touching the DB. The installer runs this BEFORE starting the API service so the service's first boot
// reuses the cert instead of generating it under the ~30s SCM start timeout. Usage:
//   ClinicManagement.API.exe provision-cert
if (args.Length > 0 && string.Equals(args[0], ProvisionCertCommand.CommandName, StringComparison.OrdinalIgnoreCase))
{
    return ProvisionCertCommand.Run(args);
}

// Determine auth mode early (before Serilog is configured) so Local installs can anchor the log file to
// the install directory (R-6) — a Windows service's CWD is System32, where a relative "logs/" path would
// scatter or fail. Cloud keeps its prior relative path, byte-for-byte. This early config is also the seam
// used for the outer-catch startup-failure handling below (both need the mode before builder.Build()).
var startupConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        optional: true)
    .AddEnvironmentVariables()
    .Build();
var startupIsLocalMode = ClinicManagement.Infrastructure.Auth.LocalAuthConfig.IsLocalMode(startupConfig);
var logFilePath = startupIsLocalMode
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

    // Use Serilog for logging
    builder.Host.UseSerilog();

    // Local installs (Phase 5 S2): run as an auto-starting Windows service. UseWindowsService() also sets
    // the content root to the install directory. Gated on mode so Cloud (and console/dev) is unaffected;
    // it is additionally a no-op when the process was not launched as a Windows service.
    if (startupIsLocalMode)
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

    // JWT Authentication — mode-branched (Auth:Mode = Cloud | Local, default Cloud).
    // Cloud: validate Auth0-issued tokens (unchanged). Local: validate app-issued tokens
    // signed with the per-install key. Authorization policies are the same in both modes.
    var isLocalAuthMode = ClinicManagement.Infrastructure.Auth.LocalAuthConfig.IsLocalMode(builder.Configuration);
    var auth0Domain = builder.Configuration["Auth0:Domain"];
    var auth0Audience = builder.Configuration["Auth0:Audience"];

    var authConfigured = false;

    if (isLocalAuthMode)
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

    if (authConfigured)
    {
        builder.Services.AddAuthorization(options =>
        {
            // Local mode (FR-E3 release gate): install a fail-closed fallback policy so every endpoint
            // without an explicit [AllowAnonymous] requires an authenticated session. Cloud stays unchanged.
            AuthorizationPolicies.ConfigurePolicies(options, isLocalAuthMode);
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

    builder.Services.AddHangfire(config =>
        config.UsePostgreSqlStorage(hangfireConnectionString));
    builder.Services.AddHangfireServer();

    // Local installs run as a Windows service, where the SCM kills any service that does not report
    // "running" within its ~30s start timeout. Applying the migrations synchronously on a fresh DB (see
    // the migrate block below) exceeded that on first boot and the service was killed before Kestrel
    // bound. In Local mode, defer migrations to a post-startup hosted service so the host reports
    // "started" as soon as it binds; Cloud keeps the synchronous migrate (no service-start timeout).
    if (isLocalAuthMode)
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
    // (NEXT_PUBLIC_API_URL=/api, same-origin) and TLS terminates once, inside the audited .NET app. Cloud
    // installs no proxy (absolute API URL, separate origins) and is unchanged.
    if (isLocalAuthMode)
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
    // "generated" | "configured" | "cloud" — logged in the startup transport posture (S3 step 4).
    var certSource = "cloud";

    if (isLocalAuthMode)
    {
        // LOCAL: always serve HTTPS. If a cert path is explicitly configured it MUST exist — refuse the
        // silent HTTP downgrade (Phase 4 Finding 2 / fail closed & loud). Otherwise self-generate a CA +
        // server cert into .local/ (FR-E2). Gated on the *mode*, never on a capability flag (Finding 4).
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

        var serverCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath, certPassword);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Plain HTTP stays LOOPBACK-only (Finding 2): the sole legitimate consumer is the co-located
            // Next BFF over http://localhost:5000. Binding it on every LAN interface would expose the
            // cleartext API (incl. POST /api/auth/login) if the firewall rule is removed/disabled. The
            // LAN-facing surface is the HTTPS front door only.
            kestrel.ListenLocalhost(httpPort);
            kestrel.ListenAnyIP(httpsPort, listen => listen.UseHttps(serverCertificate));
        });
        builder.Services.AddHttpsRedirection(options => options.HttpsPort = httpsPort);
    }
    else
    {
        // CLOUD — byte-for-byte unchanged: opt-in HTTPS only when a cert file exists, else honor Hosting:Urls.
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
            var hostingUrls = builder.Configuration["Hosting:Urls"];
            if (!string.IsNullOrWhiteSpace(hostingUrls))
            {
                builder.WebHost.UseUrls(hostingUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
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

    // Redirect HTTP → HTTPS — CLOUD only. In LOCAL mode we must NOT redirect: the only HTTP consumer is
    // the co-located Next BFF calling the API over http://localhost:5000 (loopback). Redirecting that to
    // the self-signed HTTPS front door makes Node reject the untrusted cert, surfacing as
    // "cannot reach the clinic server" on login. Local's LAN surface is HTTPS-only by bind (5001) and the
    // HTTP port is loopback-only, so there is no external HTTP client that needs redirecting.
    if (!isLocalAuthMode)
    {
        app.UseHttpsRedirection();
    }
    app.UseCors("AllowAll");
    
    // Exception handling middleware (must be before authentication/authorization)
    app.UseMiddleware<ExceptionMiddleware>();
    
    app.UseAuthentication();
    app.UseAuthorization();

    // Local mode: the app-issued JWT is stateless, so enforce account state per request —
    // revoke deactivated accounts and gate users with a pending forced password change.
    if (isLocalAuthMode)
    {
        app.UseMiddleware<ClinicManagement.API.Middleware.LocalAuthEnforcementMiddleware>();
    }

    app.MapControllers();

    // Real-time hub. A literal route, so it is more specific than the Local-mode YARP catch-all below
    // and wins endpoint selection (the WebSocket reaches the hub in-process, not the Next proxy).
    app.MapHub<ClinicHub>("/hub/clinic");

    // Hangfire Dashboard — in Local mode, reachable only from the server PC itself (loopback);
    // Cloud keeps the previous behavior (FR-E3). Mode is passed into the filter so the decision is
    // per-request (the filter is a singleton attached to the dashboard middleware).
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter() }
    });

    // Same-origin front door (Local): forward every non-/api route to the localhost Next server. The
    // catch-all is the least-specific endpoint, so /api/* controllers and the loopback-gated /hangfire
    // middleware take precedence. Cloud maps no proxy (unchanged).
    if (isLocalAuthMode)
    {
        // The proxy serves the Next web app (login/setup pages, static assets, /bff/* auth routes), which
        // handles its OWN authentication. It must be AllowAnonymous — otherwise the Local-mode fail-closed
        // FallbackPolicy (RequireAuthenticatedUser) gates every page request and returns 401 before the user
        // can even reach the login page. Only /api/* controllers stay behind [Authorize].
        app.MapReverseProxy().AllowAnonymous();
    }

    // Ensure database is created (FR-F3: migrations apply automatically on startup).
    // CLOUD: run migrations synchronously here (no Windows-service start timeout applies) — byte-for-byte
    // as before. LOCAL: skip here — migrations are deferred to DeferredStartupService (registered above)
    // so the Windows service reports "started" to the SCM before the migrations run; a fresh-DB first
    // boot otherwise exceeds the ~30s service-start timeout and the API is killed mid-migration.
    if (!isLocalAuthMode)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();

        // Backfill per-clinic reference catalogs for any existing clinic missing one (#5). Idempotent —
        // new clinics are seeded on creation; this covers clinics that predate the per-clinic conversion.
        var catalogSeeder = scope.ServiceProvider.GetRequiredService<IClinicCatalogSeeder>();
        await catalogSeeder.SeedAllClinicsAsync();
    }


    // Schedule background jobs
    // SMS/WhatsApp appointment-reminder dispatcher — minutely, connectivity-gated (see NotificationJob).
    // Sends only when the server has internet; otherwise it no-ops and leaves rows Pending, so it is safe
    // to run unconditionally (it does nothing until a Reminders channel + credentials are configured).
    RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.NotificationJob>(
        "process-notifications",
        job => job.ProcessPendingNotifications(),
        Cron.Minutely);

    // TTN « El Fatoora » outbox dispatcher — minutely, connectivity-gated (see EInvoiceOutboxJob). Sends
    // queued e-invoices only when the server has internet; otherwise no-ops and leaves them queued. Safe to
    // run unconditionally (does nothing until a clinic enables e-invoicing and queues an invoice).
    RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.EInvoiceOutboxJob>(
        "dispatch-einvoices",
        job => job.DispatchQueuedInvoices(),
        Cron.Minutely);

    // Google→App calendar sync never runs on a schedule: the recurring job and its GoogleCalendarSyncJob
    // class were removed as dead scaffolding. App→Google sync runs inline on appointment create/update, and
    // Google→App stays manual-only (GoogleCalendarController). Defensively drop any stale recurring
    // registration a previous deploy may have left in Hangfire storage so it can't fire a deleted job type.
    RecurringJob.RemoveIfExists("sync-google-calendar");

    // Log the transport posture on startup so it is observable (S3 step 4 / fail-loud-and-observable).
    if (isLocalAuthMode)
    {
        Log.Information(
            "Transport posture (Local): HTTPS on port {HttpsPort} (HTTP {HttpPort} redirects), certificate source: {CertSource}.",
            httpsPort, httpPort, certSource);
    }

    Log.Information("Clinic Management API started successfully");
    app.Run();
}
catch (Exception ex) when (startupIsLocalMode && StartupDiagnostics.IsAddressInUse(ex))
{
    // FR-F5 (Local): the bind port is already taken — name it and exit non-zero with a clear message
    // instead of a raw AddressInUseException. Uses the early startup config (the in-try mode flag is out
    // of scope here). Cloud (filter false) falls through to the fatal-rethrow below, unchanged.
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
