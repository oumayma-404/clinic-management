using ClinicManagement.Application;
using ClinicManagement.API.Maintenance;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Persistence;
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
        path: "logs/clinic-management-.log",
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

    // --- LAN hosting: config-driven bind + optional HTTPS serving (FR-E2, FR-E4) ---
    // HTTPS is opt-in: when a server certificate is configured we bind an HTTPS endpoint using it and
    // enable the redirect below; otherwise we stay on plain HTTP — the safe default so an HTTP-only LAN
    // deployment isn't broken by a failed redirect. The local CA + server-cert *generation* at install,
    // and the client-side CA trust import, are Phase 5; Phase 4 serves against any operator-supplied cert.
    var httpsCertPath = builder.Configuration["Https:CertPath"];
    var httpsCertPassword = builder.Configuration["Https:CertPassword"];
    var httpsConfigured = !string.IsNullOrWhiteSpace(httpsCertPath) && System.IO.File.Exists(httpsCertPath);

    if (httpsConfigured)
    {
        // Bind explicitly so the redirect target port is deterministic. Both endpoints listen on all
        // interfaces so LAN clients can connect (FR-E1/FR-E4). Ports are configuration-driven.
        var httpPort = builder.Configuration.GetValue<int?>("Hosting:HttpPort") ?? 5000;
        var httpsPort = builder.Configuration.GetValue<int?>("Hosting:HttpsPort") ?? 5001;
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
        // No cert: honor an explicit Hosting:Urls bind (e.g. "http://0.0.0.0:5000") so the LAN bind
        // isn't hardcoded to localhost. ASPNETCORE_URLS / the "urls" key remain honored by default.
        var hostingUrls = builder.Configuration["Hosting:Urls"];
        if (!string.IsNullOrWhiteSpace(hostingUrls))
        {
            builder.WebHost.UseUrls(hostingUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }

    var app = builder.Build();

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Guarded (R-3): only redirect to HTTPS when an HTTPS endpoint is actually configured. An
    // unconditional UseHttpsRedirection breaks a plain-HTTP LAN deployment ("failed to determine the
    // https port for redirect"). When no cert is supplied we intentionally serve HTTP only.
    if (httpsConfigured)
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

    // Hangfire Dashboard — in Local mode, reachable only from the server PC itself (loopback);
    // Cloud keeps the previous behavior (FR-E3). Mode is passed into the filter so the decision is
    // per-request (the filter is a singleton attached to the dashboard middleware).
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter(isLocalAuthMode) }
    });

    // Ensure database is created
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
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

    Log.Information("Clinic Management API started successfully");
    app.Run();
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
