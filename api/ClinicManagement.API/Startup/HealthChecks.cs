using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClinicManagement.API.Startup;

/// <summary>
/// The <c>/health</c> endpoint (multi-tenant-cloud US-6): can this instance reach its database, and its file
/// storage?
///
/// <para><b>Why it exists.</b> A hosted deployment is fronted by something that has to decide whether an
/// instance may take traffic — a container orchestrator's health probe, a compose <c>healthcheck</c>, Caddy — and
/// until now the only answer available was « the port accepts a TCP connection », which a process whose database
/// credentials are wrong answers just as cheerfully as a working one. On the clinic's own PC the operator sees the
/// console; in a datacentre nobody is looking.</para>
///
/// <para><b>Unconditional in every profile.</b> It asks no capability: « can I reach my database » is worth
/// answering on a Windows service too, where the same endpoint gives the installer's smoke test something to
/// poll that is not a login.</para>
/// </summary>
public static class HealthChecks
{
    /// <summary>The route. Deliberately <b>outside</b> <c>/api</c> — see <see cref="Register"/>.</summary>
    public const string Path = "/health";

    public const string DatabaseCheckName = "database";
    public const string StorageCheckName = "storage";

    /// <summary>Registers both checks. Call before <c>Build()</c>.</summary>
    public static void AddConfiguredHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(DatabaseCheckName)
            .AddCheck<FileStorageHealthCheck>(StorageCheckName);
    }

    /// <summary>
    /// Maps the endpoint, anonymous and un-rate-limited.
    ///
    /// <para><b>Both exemptions are required, not tidy.</b> Where the authorization fallback is fail-closed
    /// (<c>FailClosedAuthz</c>) anything without an explicit <c>AllowAnonymous</c> answers 401 — and a probe that
    /// has to hold a token is a probe nobody wires up. The limiter matters for the same reason in reverse: a
    /// per-address bucket shared with an orchestrator polling every few seconds would eventually 429, and a 429
    /// reads to the orchestrator exactly like « unhealthy ».</para>
    ///
    /// <para>⚠️ <b>The body carries check names and statuses only.</b> The reason a check failed — a connection
    /// string, a host name, a credential error — goes to the log, never to an anonymous caller. That is also why
    /// this writes its own response rather than taking the default: the framework's default body is the single
    /// word « Healthy », which cannot say <i>which</i> half is down when the answer is 200-but-degraded.</para>
    /// </summary>
    public static void Register(WebApplication app)
    {
        app.MapHealthChecks(Path, new HealthCheckOptions
        {
            ResponseWriter = WriteResponse
        }).AllowAnonymous();
    }

    /// <summary>
    /// Serialises the report as <c>{ "status": …, "checks": { "database": …, "storage": … } }</c>.
    /// The status code is the framework's: 200 for Healthy <b>and Degraded</b>, 503 for Unhealthy — which is
    /// exactly the distinction the two checks are graded on (see each one's summary).
    /// </summary>
    private static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}

/// <summary>
/// Can this instance reach PostgreSQL? <b>Unhealthy</b> when not: with no database the process can serve nothing,
/// so an orchestrator should stop sending it traffic and a compose healthcheck should restart it.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(ApplicationDbContext context, ILogger<DatabaseHealthCheck> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // A trivial round trip rather than CanConnectAsync, which swallows the exception and returns a bare
            // bool — the reason is the only part of this worth logging. It touches no entity, so the query
            // filters (and the Unset tenant scope a request-less check runs under) are not in play.
            await _context.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check: the database is unreachable.");
            return HealthCheckResult.Unhealthy("La base de données est injoignable.", ex);
        }
    }
}

/// <summary>
/// Can this instance reach its file storage? <b>Degraded</b> when not — deliberately, so the endpoint still
/// answers 200.
///
/// <para>A clinic whose object storage is down can still take appointments, record fiches, collect money and
/// print: what breaks is uploading and downloading blobs. Grading that Unhealthy would pull every instance out of
/// rotation and turn a partial outage into a total one — and restarting the API does not bring MinIO back.</para>
/// </summary>
public sealed class FileStorageHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;
    private readonly ILogger<FileStorageHealthCheck> _logger;

    // Resolved inside the check rather than injected, because RESOLUTION IS PART OF WHAT IS BEING CHECKED: where
    // MinIO is unconfigured, AddInfrastructure deliberately registers an IFileStorage factory that throws, so a
    // constructor-injected one would throw while the framework was building this check — a 500 from /health
    // instead of a health report saying which half is down.
    public FileStorageHealthCheck(IServiceProvider services, ILogger<FileStorageHealthCheck> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var storage = _services.GetRequiredService<IFileStorage>();
            await storage.ProbeAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check: the file storage is unreachable.");
            return HealthCheckResult.Degraded("Le stockage des fichiers est injoignable.", ex);
        }
    }
}
