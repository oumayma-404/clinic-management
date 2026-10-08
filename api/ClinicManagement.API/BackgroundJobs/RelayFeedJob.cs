using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Queries;
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
/// <para>A <see cref="BackgroundService"/> and not a Hangfire job: the heartbeat's rhythm is what the cloud reads as
/// « this PC is alive », and a queue that can lag or retry would make it lie. ⚠️ It waits for the deferred migrations
/// before its first tick — applying a copy onto a schema one migration behind fails, and would be read as a broken
/// copy rather than a slow start.</para>
/// </summary>
public sealed class RelayFeedJob : BackgroundService
{
    public const string ActorName = "relay-feed";

    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    /// <summary>The machine facts barely move; reading the certificate every ten seconds would be waste.</summary>
    private static readonly TimeSpan HostFactsTtl = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly IDataProtectionProvider _protection;
    private readonly IUserSecretProtector _secrets;
    private readonly IRelayBuildInfo _build;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RelayFeedJob> _logger;

    private RelayFollower? _follower;
    private Guid _followerRelayId;
    private RelayHostReport? _hostFacts;
    private DateTime _hostFactsReadAtUtc;
    private bool _schemaReady;
    private bool _toldUnpaired;

    public RelayFeedJob(
        IServiceScopeFactory scopes,
        IDataProtectionProvider protection,
        IUserSecretProtector secrets,
        IRelayBuildInfo build,
        IHttpClientFactory http,
        IConfiguration configuration,
        ILogger<RelayFeedJob> logger)
    {
        _scopes = scopes;
        _protection = protection;
        _secrets = secrets;
        _build = build;
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
    /// AC-6.1: a PC shut down, restarted or updated properly tells the cloud first, so nothing is locked. The loop stops,
    /// then the PC stands down (D14) — two quick exchanges; a cloud that does not answer leaves the fence to the clock.
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
                services.GetRequiredService<IFileStorage>()),
            cancellationToken);
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
                _logger);
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
                RelayHostFacts.LanAddresses(), RelayHostFacts.CertificateFingerprint(), RelayHostFacts.FreeBytes(storage));
            _hostFactsReadAtUtc = now;
        }

        return _hostFacts;
    }
}
