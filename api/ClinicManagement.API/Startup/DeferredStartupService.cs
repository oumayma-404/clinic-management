using ClinicManagement.Application.Common.Interfaces;
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
            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // L4f — back up BEFORE migrating, and abort the migration if that backup fails.
            //
            // The README already tells operators that the last migration is "lossy by design" with an empty
            // Down(), so « rollback means restoring this backup » — a sentence that was true only if such a
            // backup existed, and nothing took one. An upgrade is the single most likely moment for a clinic to
            // need one and the single least likely moment for anyone to have pressed the button.
            if (!await TryBackupBeforeMigratingAsync(scope.ServiceProvider, context, cancellationToken))
            {
                return; // the helper has already reported and stopped the application
            }

            _logger.LogInformation("Applying database migrations (deferred, post-startup)...");
            await context.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Database migrations applied; API fully ready.");

            // Backfill per-clinic reference catalogs for any existing clinic missing one (#5). Idempotent —
            // a clinic that already has its catalog is skipped; new clinics are seeded on creation instead.
            var catalogSeeder = scope.ServiceProvider.GetRequiredService<IClinicCatalogSeeder>();
            await catalogSeeder.SeedAllClinicsAsync(cancellationToken);
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

    /// <summary>
    /// Takes a pre-migration backup when there is actually something to migrate (L4f). Returns whether the
    /// caller may proceed.
    ///
    /// <para><b>Only when a migration is pending.</b> Every ordinary restart runs this path, and dumping the whole
    /// database on each one would turn a service restart into a minutes-long operation and fill the destination
    /// with identical copies. <c>GetPendingMigrationsAsync</c> is what makes « before a schema change » literal.</para>
    ///
    /// <para><b>A failed backup aborts the migration</b>, loudly, through the same <c>StartupDiagnostics</c>
    /// channel an unreachable database uses. That is the whole point: migrating anyway would apply an
    /// irreversible change with no way back, which is strictly worse than not starting. The one exception is a
    /// <b>fresh</b> database with no schema yet — there is nothing to lose and nothing pg_dump could meaningfully
    /// capture, and refusing to install the product because it cannot back up an empty database would be absurd.</para>
    /// </summary>
    private async Task<bool> TryBackupBeforeMigratingAsync(
        IServiceProvider scopedServices, ApplicationDbContext context, CancellationToken cancellationToken)
    {
        IEnumerable<string> pending;
        try
        {
            pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        }
        catch
        {
            // Cannot even ask — let the migrate call below produce the real, classified failure.
            return true;
        }

        if (!pending.Any())
        {
            return true;
        }

        var applied = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (!applied.Any())
        {
            _logger.LogInformation(
                "First-run database: {Count} migration(s) pending and nothing to back up yet.", pending.Count());
            return true;
        }

        _logger.LogInformation(
            "{Count} migration(s) pending — taking a pre-migration backup first (L4f).", pending.Count());

        try
        {
            var backupService = scopedServices.GetRequiredService<IBackupService>();
            var result = await backupService.CreateBackupAsync(destinationFolder: null, cancellationToken);
            _logger.LogInformation(
                "Pre-migration backup written to {Path} ({Objects} objects verified).",
                result.DestinationPath, result.VerifiedObjectCount);
            return true;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.ReportFatal(
                "Mise à jour interrompue : la sauvegarde préalable à la migration de la base a échoué "
                + $"({ex.Message}) La base n'a PAS été modifiée. Corrigez le dossier de sauvegarde "
                + "(Paramètres → Sauvegarde, ou la clé Backup:DefaultDestination) puis redémarrez le service.",
                ex);
            _lifetime.StopApplication();
            return false;
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
