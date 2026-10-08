using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>How one attempt at fetching the cloud's installer ended.</summary>
public enum RelayInstallerFetchStatus
{
    /// <summary>The part file now holds the whole installer the cloud served.</summary>
    Complete,

    /// <summary>404: the cloud does not offer the installer of its build yet.</summary>
    NotPublished,

    /// <summary>The cloud served another build than the one asked for — it was redeployed meanwhile.</summary>
    OtherBuild,

    /// <summary>No answer, a cut, a stall, no hash: what arrived is kept and the next attempt resumes it.</summary>
    Interrupted,
}

public sealed record RelayInstallerFetch(RelayInstallerFetchStatus Status, string? Sha256 = null, string? Error = null);

/// <summary>Where the PC de secours gets the cloud's installer (<c>GET /api/relay/installer</c>).</summary>
public interface IRelayInstallerSource
{
    /// <summary>Resumes <paramref name="partPath"/> from its length with the cloud's installer of <paramref name="build"/>. Never throws.</summary>
    Task<RelayInstallerFetch> FetchAsync(string partPath, string build, CancellationToken cancellationToken);
}

public sealed record RelayLaunch(bool Started, string? Error = null);

/// <summary>Starts the installer so that it outlives the API service it is about to stop and replace.</summary>
public interface IRelayUpdateLauncher
{
    RelayLaunch Launch(string installerPath, string arguments, string workingDirectory);

    /// <summary>Removes whatever <see cref="Launch"/> left registered. Best-effort.</summary>
    void Forget();
}

/// <summary>
/// The PC de secours follows the cloud's build by itself (<c>clinic-pc-copy</c> D10b): after a cloud deploy the
/// heartbeat says « update needed », the copy waits, and this downloads the cloud's own installer, checks it against
/// the hash the cloud sent beside it, and runs it <c>/RELAY</c> with no code — an update in place that keeps the
/// pairing. The new build heartbeats, the cloud stops saying « update needed », and the copy catches up by itself.
///
/// <para>⚠️ <b>The download runs beside the ticks, never inside one.</b> The installer is a couple of hundred
/// megabytes, and a tick that waited for it would stop the heartbeat — the cloud reads two silent minutes as
/// « Éteint » and rings the admins. It resumes where it stopped (<c>Range</c>), across a cut and a restart.</para>
///
/// <para>⚠️ <b>One cloud build, one run of the installer.</b> The start is saved before it happens; an installer that
/// refused or never came back is reported (<see cref="RelayFollowerState.UpdateError"/>) and not run again — running
/// it in a loop would stop and start the cabinet's services for ever. The next cloud build starts a new series.</para>
/// </summary>
public sealed class RelayUpdater
{
    public const string FolderName = "updates";
    public const string InstallerFileName = "APEXA-PC-de-secours-mise-a-jour.exe";
    public const string ResultFileName = "resultat-mise-a-jour.txt";

    /// <summary>The cloud has no installer for its build yet: asked again after this, never in a loop.</summary>
    public static readonly TimeSpan NotPublishedRetry = TimeSpan.FromMinutes(5);

    /// <summary>A cut or stalled download resumes after this.</summary>
    public static readonly TimeSpan InterruptedRetry = TimeSpan.FromMinutes(1);

    /// <summary>A file that does not match its hash is thrown away and fetched again after this.</summary>
    public static readonly TimeSpan BadInstallerRetry = TimeSpan.FromMinutes(5);

    /// <summary>An update still downloading after this stops being « Mise à jour » and reads as late — somebody should know.</summary>
    public static readonly TimeSpan ClaimsUpdatingFor = TimeSpan.FromHours(1);

    /// <summary>Still on the old build this long after the start: the installer did not land.</summary>
    public static readonly TimeSpan LaunchGivesUpAfter = TimeSpan.FromMinutes(30);

    /// <summary>The new build leaves the installer alone this long — it is still finishing after starting the services.</summary>
    public static readonly TimeSpan CleanupAfterLanding = TimeSpan.FromMinutes(10);

    public const string NotLandedSentence =
        "La mise à jour du PC de secours n'a pas abouti : il fonctionne toujours sur l'ancienne version et ne copie plus le cabinet.";

    public const string NotStartedSentence =
        "Windows n'a pas pu lancer la mise à jour du PC de secours : il ne copie plus le cabinet.";

    private readonly IRelayInstallerSource _source;
    private readonly IRelayUpdateLauncher _launcher;
    private readonly string _folder;
    private readonly string _logFolder;
    private readonly ILogger _logger;
    private readonly Func<DateTime> _utcNow;

    private Task<RelayInstallerFetch>? _download;
    private CancellationTokenSource? _downloadCancel;

    public RelayUpdater(
        IRelayInstallerSource source,
        IRelayUpdateLauncher launcher,
        string folder,
        string logFolder,
        ILogger logger,
        Func<DateTime>? utcNow = null)
    {
        _source = source;
        _launcher = launcher;
        _folder = folder;
        _logFolder = logFolder;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string InstallerPath => Path.Combine(_folder, InstallerFileName);

    private string PartPath => InstallerPath + ".part";

    private string ResultPath => Path.Combine(_folder, ResultFileName);

    /// <summary>
    /// What the heartbeat says (« Mise à jour » on the card): true while an update is genuinely under way — not while
    /// the cloud has nothing to update to, not once it failed, not once this process IS the build it was fetching (the
    /// new build's first heartbeat still reads the old file), and not for ever.
    /// </summary>
    public static bool ReportsUpdating(RelayFollowerState state, string currentBuild, DateTime nowUtc) =>
        state.UpdateNeeded && state.UpdateBuild is not null && state.UpdateError is null
        && !string.Equals(state.UpdateBuild, currentBuild, StringComparison.Ordinal)
        && (state.UpdateLaunchedAtUtc is { } launched
            ? nowUtc - launched < LaunchGivesUpAfter
            : !state.UpdateWaitingForInstaller && state.UpdateStartedAtUtc is { } started && nowUtc - started < ClaimsUpdatingFor);

    /// <summary>
    /// The installer's update in place (<c>clinic-setup.iss</c>): <c>/RELAY</c> with no <c>/PAIRFILE</c> keeps the
    /// pairing the PC has. Each <c>/NAME=value</c> is one quoted argument, so a path with spaces survives.
    /// </summary>
    public static string Arguments(string resultFile, string logFile) =>
        string.Join(' ',
            "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/RELAY",
            Quoted("/RESULTFILE=" + resultFile),
            Quoted("/LOG=" + logFile));

    /// <summary>One step of the update, on a tick whose heartbeat said the cloud runs <paramref name="cloudBuild"/>.</summary>
    public async Task<RelayFollowerState> StepAsync(
        RelayFollowerState state,
        string cloudBuild,
        Func<RelayFollowerState, RelayFollowerState> save,
        Func<RelayFollowerState, CancellationToken, Task> announceUpdating,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cloudBuild))
        {
            return state;
        }

        var now = _utcNow();
        if (!string.Equals(state.UpdateBuild, cloudBuild, StringComparison.Ordinal))
        {
            // A new cloud build starts a new series: what was fetched for another build is useless.
            await AbandonDownloadAsync();
            ClearFolder();
            _logger.LogInformation("PC de secours: the cloud runs build {CloudBuild}; this PC updates to it.", cloudBuild);
            state = WithoutSeries(state) with { UpdateBuild = cloudBuild, UpdateStartedAtUtc = now };
        }

        if (state.UpdateError is not null)
        {
            return state;
        }

        if (state.UpdateLaunchedAtUtc is { } launched)
        {
            return AfterLaunch(state, launched, now);
        }

        if (state.UpdateRetryAfterUtc is { } retry && now < retry)
        {
            return state;
        }

        if (!File.Exists(InstallerPath))
        {
            _download ??= StartDownload(cloudBuild, cancellationToken);
            if (!_download.IsCompleted)
            {
                return state;
            }

            state = Received(state, await TakeDownloadAsync(), now);
            if (!File.Exists(InstallerPath))
            {
                return state;
            }
        }

        return await LaunchAsync(state, save, announceUpdating, now, cancellationToken);
    }

    /// <summary>
    /// The cloud no longer asks for an update — it landed (this process IS the build the series fetched), or the cloud
    /// came back to this PC's build. The series is closed, once the installer has had time to finish.
    /// </summary>
    public async Task<RelayFollowerState> SettleAsync(RelayFollowerState state)
    {
        if (state.UpdateBuild is null)
        {
            return state;
        }

        if (state.UpdateLaunchedAtUtc is { } launched && _utcNow() - launched < CleanupAfterLanding)
        {
            return state;
        }

        await AbandonDownloadAsync();
        _launcher.Forget();
        ClearFolder();
        if (state.UpdateLaunchedAtUtc is not null)
        {
            _logger.LogInformation("PC de secours: updated to build {Build}; the copy resumes.", state.UpdateBuild);
        }

        return WithoutSeries(state);
    }

    private RelayFollowerState Received(RelayFollowerState state, RelayInstallerFetch fetch, DateTime now)
    {
        switch (fetch.Status)
        {
            case RelayInstallerFetchStatus.Complete:
                File.Move(PartPath, InstallerPath, overwrite: true);
                return state with { UpdateSha256 = fetch.Sha256, UpdateWaitingForInstaller = false, UpdateRetryAfterUtc = null };

            case RelayInstallerFetchStatus.NotPublished:
                _logger.LogInformation("PC de secours: the cloud does not offer the installer of build {Build} yet.", state.UpdateBuild);
                return state with { UpdateWaitingForInstaller = true, UpdateRetryAfterUtc = now + NotPublishedRetry };

            case RelayInstallerFetchStatus.OtherBuild:
                // The cloud was redeployed under the download: the next heartbeat names its build and starts that series.
                TryDelete(PartPath);
                return state with { UpdateRetryAfterUtc = now + InterruptedRetry };

            default:
                _logger.LogWarning("PC de secours: the installer download stopped ({Reason}); it resumes shortly.", fetch.Error);
                return state with { UpdateWaitingForInstaller = false, UpdateRetryAfterUtc = now + InterruptedRetry };
        }
    }

    private async Task<RelayFollowerState> LaunchAsync(
        RelayFollowerState state,
        Func<RelayFollowerState, RelayFollowerState> save,
        Func<RelayFollowerState, CancellationToken, Task> announceUpdating,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Held open, sharing read only, from the hash check until the file is handed to Windows. `.local` already admits
        // only SYSTEM and the administrators; this closes the swap window for them too.
        await using var hold = new FileStream(InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(hold, cancellationToken));
        if (state.UpdateSha256 is null || !string.Equals(actual, state.UpdateSha256, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("PC de secours: the downloaded installer does not match the hash the cloud sent; it is not run.");
            await hold.DisposeAsync();
            TryDelete(InstallerPath);
            return state with { UpdateSha256 = null, UpdateRetryAfterUtc = now + BadInstallerRetry };
        }

        if (!await DisarmBeforeUpdateAsync(cancellationToken))
        {
            return state;
        }

        // « Mise à jour » is the last thing the cloud hears before the installer stops this service.
        await announceUpdating(state, cancellationToken);
        state = save(state with { UpdateLaunchedAtUtc = now });

        Directory.CreateDirectory(_logFolder);
        TryDelete(ResultPath);
        var launch = _launcher.Launch(InstallerPath,
            Arguments(ResultPath, Path.Combine(_logFolder, $"relay-update-{now:yyyyMMdd-HHmmss}.log")), _folder);
        if (!launch.Started)
        {
            _logger.LogError("PC de secours: the update installer could not be started: {Reason}", launch.Error);
            return state with { UpdateError = NotStartedSentence };
        }

        _logger.LogWarning("PC de secours: the update to build {Build} is running; this service stops and comes back on it.",
            state.UpdateBuild);
        return state;
    }

    /// <summary>
    /// [D10b / D14] The write lease is disarmed here, before the installer stops this PC's services, so that a planned
    /// restart never reads as a cut. Part 2 (« La relève ») brings the lease; until then there is nothing to disarm.
    /// </summary>
    private static Task<bool> DisarmBeforeUpdateAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    private RelayFollowerState AfterLaunch(RelayFollowerState state, DateTime launched, DateTime now)
    {
        // Still running the old build. On success the installer stops this service before it writes anything, so a
        // result sentence seen from here is a refusal or a failure, in the installer's own words.
        if (ReadResult() is { } sentence)
        {
            _logger.LogError("PC de secours: the update installer did not land: {Sentence}", sentence);
            return state with { UpdateError = "La mise à jour du PC de secours n'a pas pu s'installer : " + sentence };
        }

        if (now - launched > LaunchGivesUpAfter)
        {
            _logger.LogError("PC de secours: still on the old build {Minutes} min after the update started.",
                (int)(now - launched).TotalMinutes);
            return state with { UpdateError = NotLandedSentence };
        }

        return state;
    }

    private Task<RelayInstallerFetch> StartDownload(string build, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_folder);
        _downloadCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return DownloadAsync(build, _downloadCancel.Token);
    }

    private async Task<RelayInstallerFetch> DownloadAsync(string build, CancellationToken cancellationToken)
    {
        try
        {
            return await _source.FetchAsync(PartPath, build, cancellationToken);
        }
        catch (Exception ex)
        {
            // The source never throws by contract; a broken one must still not end the series.
            return new RelayInstallerFetch(RelayInstallerFetchStatus.Interrupted, Error: ex.Message);
        }
    }

    private async Task<RelayInstallerFetch> TakeDownloadAsync()
    {
        var fetch = await _download!;
        _download = null;
        _downloadCancel?.Dispose();
        _downloadCancel = null;
        return fetch;
    }

    private async Task AbandonDownloadAsync()
    {
        if (_download is null)
        {
            return;
        }

        _downloadCancel?.Cancel();
        await TakeDownloadAsync();
    }

    private string? ReadResult()
    {
        try
        {
            return File.Exists(ResultPath)
                ? File.ReadAllLines(ResultPath, Encoding.UTF8).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim()
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void ClearFolder()
    {
        if (!Directory.Exists(_folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_folder))
        {
            TryDelete(file);
        }
    }

    private static RelayFollowerState WithoutSeries(RelayFollowerState state) => state with
    {
        UpdateBuild = null,
        UpdateStartedAtUtc = null,
        UpdateWaitingForInstaller = false,
        UpdateSha256 = null,
        UpdateRetryAfterUtc = null,
        UpdateLaunchedAtUtc = null,
        UpdateError = null,
    };

    private static string Quoted(string argument) => "\"" + argument + "\"";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still held by a finishing installer; the next series clears the folder.
        }
    }
}
