using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Runs the one-time, potentially-slow startup work (EF Core migrations) OFF the host's startup
/// critical path in Local mode.
///
/// In Local mode the API runs as a Windows service, and the Service Control Manager kills any service
/// that does not report "running" within its start timeout (~30 s). Applying the ~21 migrations to a
/// fresh database — on top of the first-run JIT cost of the freshly-extracted assemblies — pushed the
/// synchronous <c>Database.Migrate()</c> that used to run in <c>Program.cs</c> past that limit, so the
/// service was killed mid-migration before Kestrel ever bound (SCM event 7009, "timeout reached").
///
/// This service returns from <see cref="StartAsync"/> immediately (the work is dispatched
/// fire-and-forget), so the host finishes starting and the SCM sees "running" as soon as Kestrel binds;
/// migrations then complete in the background a few seconds later — well before the first DB-touching
/// request (first-run setup). Registered ONLY in Local mode; Cloud keeps the synchronous migrate in
/// <c>Program.cs</c> (it is not a Windows service, so no start timeout applies), byte-for-byte.
/// </summary>
public sealed class DeferredStartupService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<DeferredStartupService> _logger;
    private Task? _initTask;

    public DeferredStartupService(
        IServiceProvider services,
        IHostApplicationLifetime lifetime,
        ILogger<DeferredStartupService> logger)
    {
        _services = services;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget: do NOT await the slow work here, or it would block the host from reporting
        // "started" to the SCM — the very timeout this service exists to avoid.
        _initTask = Task.Run(() => InitializeAsync(_lifetime.ApplicationStopping), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Applying database migrations (deferred, post-startup)...");
            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Database migrations applied; API fully ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is shutting down before migrations finished — nothing to report.
        }
        catch (Exception ex) when (StartupDiagnostics.IsDatabaseConnectionFailure(ex))
        {
            // FR-F5: an unreachable database is an operator problem, not an opaque crash. Surface a clear
            // message (console + log + Windows Event Log) and stop the app rather than serving a broken
            // instance whose requests would all fail against a missing schema.
            StartupDiagnostics.ReportFatal(StartupDiagnostics.DatabaseUnreachableMessage(), ex);
            _lifetime.StopApplication();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deferred startup migration failed; stopping the application.");
            _lifetime.StopApplication();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Let an in-flight migration observe cancellation and settle before the process exits.
        if (_initTask is not null)
        {
            try { await _initTask.WaitAsync(cancellationToken); }
            catch { /* shutting down — best effort */ }
        }
    }
}
