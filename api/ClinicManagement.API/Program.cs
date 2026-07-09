using ClinicManagement.Application;
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

    // Add Hangfire for background jobs
    var hangfireConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddHangfire(config =>
        config.UsePostgreSqlStorage(hangfireConnectionString));
    builder.Services.AddHangfireServer();

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
            var provisioner = new CertificateProvisioner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CertificateProvisioner>.Instance);
            var generated = provisioner.EnsureServerCertificate();
            certPath = generated.PfxPath;
            certPassword = generated.Password;
            certSource = "generated";
            Log.Information("Local HTTPS certificate ready; CA exported to {CaCertPath} for client trust import.", generated.CaCertPath);
        }

        var serverCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath, certPassword);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(httpPort);
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

    // Redirect HTTP → HTTPS. LOCAL now always serves HTTPS (generated or configured cert; a
    // configured-but-missing cert fails loud above, so we never reach here on plain HTTP in Local).
    // CLOUD is unchanged — it always enabled the redirect (its HTTPS posture is set by the host/proxy).
    app.UseHttpsRedirection();
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

    // Hangfire Dashboard — in Local mode, reachable only from the server PC itself (loopback);
    // Cloud keeps the previous behavior (FR-E3). Mode is passed into the filter so the decision is
    // per-request (the filter is a singleton attached to the dashboard middleware).
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter(isLocalAuthMode) }
    });

    // Same-origin front door (Local): forward every non-/api route to the localhost Next server. The
    // catch-all is the least-specific endpoint, so /api/* controllers and the loopback-gated /hangfire
    // middleware take precedence. Cloud maps no proxy (unchanged).
    if (isLocalAuthMode)
    {
        app.MapReverseProxy();
    }

    // Ensure database is created (FR-F3: migrations apply automatically on startup).
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            context.Database.Migrate();
        }
        catch (Exception ex) when (isLocalAuthMode && StartupDiagnostics.IsDatabaseConnectionFailure(ex))
        {
            // FR-F5 (Local): an unreachable database is an operator problem, not a stack trace. Surface a
            // clear message (console + log + Event Log) and exit non-zero rather than crashing opaquely.
            // Cloud keeps the fatal-rethrow below (the `when` filter is false), byte-for-byte (R-9).
            StartupDiagnostics.ReportFatal(StartupDiagnostics.DatabaseUnreachableMessage(), ex);
            return 1;
        }
    }


    // Schedule background jobs
    /*RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.NotificationJob>(
        "process-notifications",
        job => job.ProcessPendingNotifications(),
        Cron.Minutely);

    /*RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.AISummaryJob>(
        "generate-ai-summaries",
        job => job.GenerateSummariesForUpcomingAppointments(),
        Cron.Minutely);*/

    // Google Calendar sync from Google Calendar to App is disabled for now
    // We only sync from App to Google Calendar (triggered on create/update actions)
    // Remove the recurring job if it exists in Hangfire
    RecurringJob.RemoveIfExists("sync-google-calendar");
    
    // TODO: Re-enable when needed by uncommenting the lines below
    // RecurringJob.AddOrUpdate<ClinicManagement.API.BackgroundJobs.GoogleCalendarSyncJob>(
    //     "sync-google-calendar",
    //     job => job.SyncFromGoogleCalendar(),
    //     Cron.Hourly);

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
// Local mode (FR-E3): allow only requests originating from the server PC itself (loopback) — the
// dashboard exposes job internals and must not be reachable from a LAN client. Cloud mode keeps the
// prior permissive behavior (the dashboard is not exposed publicly there; tightening it is R-6/R-7).
// Reverse-proxy caveat (R-9): behind a proxy the client IP is the proxy's, not loopback — acceptable
// for the v1 single-PC topology where the API is not proxied.
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly bool _isLocalMode;

    public HangfireAuthorizationFilter(bool isLocalMode)
    {
        _isLocalMode = isLocalMode;
    }

    public bool Authorize(DashboardContext context)
    {
        if (_isLocalMode)
        {
            return ClinicManagement.Infrastructure.LocalRequest.IsLoopback(context.GetHttpContext());
        }

        return true;
    }
}
