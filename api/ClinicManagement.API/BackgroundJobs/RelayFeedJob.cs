using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Persistence;
using ClinicManagement.Infrastructure.Relay;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// The PC de secours's copy loop (<c>clinic-pc-copy</c> Part 1 « Copy »): every ten seconds, one
/// <see cref="RelayFollower"/> tick. Registered only where the deployment <c>MirrorsCloudClinic</c>.
///
/// <para>Beside it, the <b>lease loop</b> (D13): every five seconds, the takeover decision
/// (<see cref="RelayLeaseKeeper"/>) and, when a copy tick has been busy past <see cref="RelayLease.PulseAfter"/>, a
/// heartbeat of its own — a copy tick fetching a big radiograph must not read, to the cloud, as a cut.</para>
///
/// <para>A <see cref="BackgroundService"/> and not a Hangfire job: the heartbeat's rhythm is what the cloud reads as
/// « this PC is alive », and a queue that can lag or retry would make it lie. ⚠️ It waits for the deferred migrations
/// before its first tick — applying a copy onto a schema one migration behind fails, and would be read as a broken
/// copy rather than a slow start.</para>
/// </summary>
public sealed class RelayFeedJob : BackgroundService
{
    public const string ActorName = "relay-feed";

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan LeaseInterval = TimeSpan.FromSeconds(5);

    /// <summary>The machine facts barely move; reading the certificate every ten seconds would be waste.</summary>
    private static readonly TimeSpan HostFactsTtl = TimeSpan.FromMinutes(5);

    /// <summary>AC-3.11: how often the cabinet's hours are read again for Windows Update's active hours.</summary>
    public static readonly TimeSpan UpdateHoursEvery = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly IDataProtectionProvider _protection;
    private readonly IUserSecretProtector _secrets;
    private readonly IRelayBuildInfo _build;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly RelayLease _lease;
    private readonly RelayLeaseKeeper _keeper;
    private readonly ILogger<RelayFeedJob> _logger;

    private volatile RelayFollower? _follower;
    private Guid _followerRelayId;
    private RelayHostReport? _hostFacts;
    private DateTime _hostFactsReadAtUtc;
    private volatile bool _schemaReady;
    private bool _toldUnpaired;
    private readonly IRelayUpdateHoursSink _updateHours = new WindowsUpdateHours();
    private DateTime _updateHoursCheckedAtUtc;
    private RelayActiveHours? _updateHoursApplied;
    private bool _toldUpdateHours;

    public RelayFeedJob(
        IServiceScopeFactory scopes,
        IDataProtectionProvider protection,
        IUserSecretProtector secrets,
        IRelayBuildInfo build,
        IHttpClientFactory http,
        IConfiguration configuration,
        RelayLease lease,
        IRelayBoxProbe box,
        ILogger<RelayFeedJob> logger,
        IInternetProbe? internet = null)
    {
        _scopes = scopes;
        _protection = protection;
        _secrets = secrets;
        _build = build;
        _http = http;
        _configuration = configuration;
        _lease = lease;
        _keeper = new RelayLeaseKeeper(lease, new RelayFollowerStateStore(), box, logger, internet);
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(CopyLoopAsync(stoppingToken), LeaseLoopAsync(stoppingToken), PromiseLoopAsync(stoppingToken));

    /// <summary>After a failed poll — the cloud away, a cut — the next try.</summary>
    private static readonly TimeSpan PromiseRetry = TimeSpan.FromSeconds(2);

    private IRelayPromiseChannel? _promiseChannel;
    private Guid _promiseRelayId;

    /// <summary>
    /// D16: a long poll kept open on the cloud, so a note, devis or avoir number is held here before the cloud makes it
    /// final — the PC then numbers after it during a cut (EC-22). Each kept promise is acknowledged in the very next
    /// poll, opened at once: the cloud's save waits for that round trip (≤ 3 s). Idle while unpaired or holding (the
    /// cloud then numbers nothing).
    /// </summary>
    private async Task PromiseLoopAsync(CancellationToken stoppingToken)
    {
        var acks = new List<Guid>();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var credentials = _schemaReady && !_lease.IsHolding ? new RelayCredentialStore(_protection).TryLoad() : null;
                if (credentials is null)
                {
                    acks.Clear();
                    await Task.Delay(PromiseRetry, stoppingToken);
                    continue;
                }

                var call = await PromiseChannelFor(credentials).PromisesAsync(acks, stoppingToken);
                if (!call.IsOk || call.Value is null)
                {
                    await Task.Delay(PromiseRetry, stoppingToken);
                    continue;
                }

                acks.Clear();
                if (call.Value.Count > 0)
                {
                    using var scope = _scopes.CreateScope();
                    var services = scope.ServiceProvider;
                    services.GetRequiredService<ITenantScope>().UseClinic(credentials.ClinicId);
                    var mine = call.Value.Where(p => p.ClinicId == credentials.ClinicId).ToList();
                    await services.GetRequiredService<RelayNumberPromiseStore>().KeepAsync(mine, DateTime.UtcNow, stoppingToken);
                    acks.AddRange(mine.Select(p => p.Id));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PC de secours: the number-promise poll failed; retrying.");
                acks.Clear();
                try
                {
                    await Task.Delay(PromiseRetry, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private IRelayPromiseChannel PromiseChannelFor(RelayCredentials credentials)
    {
        if (_promiseChannel is null || _promiseRelayId != credentials.RelayId)
        {
            _promiseChannel = new RelayCloudClient(_http.CreateClient(nameof(RelayFeedJob)), credentials, _build.Current);
            _promiseRelayId = credentials.RelayId;
        }

        return _promiseChannel;
    }

    private async Task LeaseLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(LeaseInterval);
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                if (await _keeper.TickAsync(stoppingToken))
                {
                    await RecordTakeoverAsync(stoppingToken);
                }

                if (_schemaReady && _follower is { } follower && !_lease.IsHolding)
                {
                    await follower.PulseAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PC de secours: the lease tick failed.");
            }
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>FR-8: the takeover is a row of the cabinet's journal, written on this PC (it reaches the cloud at the return).</summary>
    private async Task RecordTakeoverAsync(CancellationToken cancellationToken)
    {
        var credentials = new RelayCredentialStore(_protection).TryLoad();
        if (credentials is null)
        {
            return;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<IAuditActorProvider>().RunAs(ActorName);
            services.GetRequiredService<ITenantScope>().UseClinic(credentials.ClinicId);
            await RelayJournal.StageOnPcAsync(services.GetRequiredService<IAuditEntryRepository>(),
                services.GetRequiredService<IAuditActorProvider>().Current, credentials.ClinicId, credentials.RelayId,
                Environment.MachineName, AuditAction.Update, RelayJournal.TookOver, _lease.HoldingSinceUtc ?? DateTime.UtcNow,
                cancellationToken);
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The takeover stands whatever the journal says: refusing the cabinet's saves over a missing row would be
            // the wrong way round.
            _logger.LogError(ex, "PC de secours: the takeover could not be written to the journal.");
        }
    }

    private async Task CopyLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // One failed tick must never end the loop: the next one is ten seconds away.
                _logger.LogError(ex, "PC de secours: the copy tick failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>How long a stopping service waits for the cloud to confirm the stand-down.</summary>
    public static readonly TimeSpan StandDownBudget = TimeSpan.FromSeconds(10);

    /// <summary>
    /// AC-6.1: a PC shut down, restarted or updated properly tells the cloud first, so nothing is locked. Both loops stop
    /// (a pulse after the stand-down would arm it again), then the PC stands down (D14) — two quick exchanges; a cloud that
    /// does not answer leaves the fence to the clock. A PC holding the cabinet's saves does not stand down.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_follower is null)
        {
            return;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(StandDownBudget);
        try
        {
            if (await _follower.StandDownAsync(budget.Token))
            {
                _logger.LogInformation("PC de secours: stood down before stopping; the cloud keeps recording the cabinet's work.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PC de secours: the stand-down before stopping did not complete.");
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var credentials = new RelayCredentialStore(_protection).TryLoad();
        if (credentials is null)
        {
            if (!_toldUnpaired)
            {
                _logger.LogWarning("PC de secours: this PC is not paired with a cloud clinic yet (no readable {File}).",
                    RelayCredentialStore.FileName);
                _toldUnpaired = true;
            }

            return;
        }

        _toldUnpaired = false;
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IAuditActorProvider>().RunAs(ActorName);
        services.GetRequiredService<ITenantScope>().UseClinic(credentials.ClinicId);

        if (!_schemaReady)
        {
            var pending = await services.GetRequiredService<ApplicationDbContext>().Database.GetPendingMigrationsAsync(cancellationToken);
            if (pending.Any())
            {
                return;
            }

            _schemaReady = true;
        }

        var follower = FollowerFor(credentials);
        await follower.TickAsync(
            new RelayLocalSide(
                services.GetRequiredService<IClinicRelayRowStore>(),
                services.GetRequiredService<IRelayBlobIndex>(),
                services.GetRequiredService<IFileStorage>(),
                services.GetRequiredService<IRelayHandbackStore>()),
            cancellationToken);

        await KeepUpdateHoursAsync(services.GetRequiredService<ApplicationDbContext>(), credentials.ClinicId, cancellationToken);
    }

    /// <summary>
    /// AC-3.11: Windows Update never restarts this PC during the cabinet's opening hours — its active hours follow them,
    /// read hourly from the copy (an admin changing the hours reaches the PC with the next copy). Best-effort: a PC that
    /// cannot set them says so once in its log; nothing else depends on it.
    /// </summary>
    private async Task KeepUpdateHoursAsync(ApplicationDbContext db, Guid clinicId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (now - _updateHoursCheckedAtUtc < UpdateHoursEvery)
        {
            return;
        }

        _updateHoursCheckedAtUtc = now;
        try
        {
            var json = await db.Clinics.AsNoTracking().Where(c => c.Id == clinicId)
                .Select(c => c.WorkingHoursJson).FirstOrDefaultAsync(cancellationToken);
            if (RelayUpdateHours.Window(json) is not { } hours || hours == _updateHoursApplied)
            {
                return;
            }

            if (_updateHours.TryApply(hours, out var error))
            {
                _updateHoursApplied = hours;
                _toldUpdateHours = false;
                _logger.LogInformation("PC de secours: Windows Update will not restart this PC between {Start}:00 and {End}:00.",
                    hours.Start, hours.End);
            }
            else if (!_toldUpdateHours)
            {
                _toldUpdateHours = true;
                _logger.LogWarning("PC de secours: Windows Update's active hours could not be set — {Error}", error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PC de secours: the cabinet's hours could not be read for Windows Update.");
        }
    }

    /// <summary>One follower per pairing, kept across ticks for its token and its file retry memory.</summary>
    private RelayFollower FollowerFor(RelayCredentials credentials)
    {
        if (_follower is null || _followerRelayId != credentials.RelayId)
        {
            var build = _build.Current;
            var updates = Path.Combine(LocalInstallPaths.LocalDir, RelayUpdater.FolderName);
            _follower = new RelayFollower(
                new RelayCloudClient(_http.CreateClient(nameof(RelayFeedJob)), credentials, build),
                new RelayFollowerStateStore(),
                credentials,
                _secrets,
                build,
                HostFacts,
                new RelayUpdater(
                    new RelayInstallerDownloader(_http.CreateClient(nameof(RelayInstallerDownloader)), credentials.ApiBase),
                    RelayUpdateLaunchers.For(_configuration["Relay:UpdateLauncher"], updates, _logger),
                    updates,
                    LocalInstallPaths.Resolve("logs"),
                    _logger),
                _lease,
                _logger,
                assertionKeys: new RelayAssertionKeyStore(_protection));
            _followerRelayId = credentials.RelayId;
        }

        return _follower;
    }

    private RelayHostReport HostFacts()
    {
        var now = DateTime.UtcNow;
        if (_hostFacts is null || now - _hostFactsReadAtUtc > HostFactsTtl)
        {
            var storage = LocalInstallPaths.Resolve(_configuration["FileStorage:BasePath"] ?? "Files");
            _hostFacts = new RelayHostReport(
                RelayHostFacts.LanAddresses(), RelayHostFacts.CertificateFingerprint(), RelayHostFacts.FreeBytes(storage),
                _configuration.GetValue<int?>("Hosting:HttpsPort") ?? 5001, RelayHostFacts.GatewayAddress(),
                _configuration.GetValue<int?>("Hosting:TrustPort") ?? Startup.TrustPortGate.DefaultPort);
            _hostFactsReadAtUtc = now;
        }

        return _hostFacts;
    }
}
