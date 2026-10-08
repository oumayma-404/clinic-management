using System.Diagnostics;
using ClinicManagement.Application.Features.Relay;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// The PC de secours hands the cut back (<c>clinic-pc-copy</c> D18, US-5, AC-5.1 → AC-5.9), one step per copy tick while
/// it holds the cabinet's saves.
///
/// <list type="number">
///   <item>The cloud has answered every heartbeat for <see cref="StableFor"/> (AC-5.1).</item>
///   <item>Saves are refused (« Retour au cloud en cours », AC-5.2), every save already past the gate is waited for, and
///   the cut is read: the PC's own log, its rows, its journal, the sign-in traces.</item>
///   <item>The files the rows name go first, then the rows (phase 1). The cloud applies them and stays fenced.</item>
///   <item>This PC drops its log of the cut and stops holding; its next heartbeat says so and the cloud takes the saves
///   back (phase 2). So the two sides are never writable at once, whatever answer is lost.</item>
/// </list>
///
/// <para>⚠️ <b>A lost answer is not a failure</b>: the same handback is sent again (same id — the cloud answers « already
/// applied ») while saves stay refused, for <see cref="RetryWithin"/>. After that the cabinet works on the PC again
/// (AC-5.9) and the next attempt gets a <b>new</b> id: the cloud may have applied the old one, and what is saved from now
/// must travel too. Re-sending rows the cloud already holds is harmless — they come back as the cabinet's own.</para>
/// </summary>
public sealed class RelayHandback
{
    /// <summary>AC-5.1: the cloud must have answered this long before the cut is handed back.</summary>
    public static readonly TimeSpan StableFor = TimeSpan.FromMinutes(2);

    /// <summary>An attempt whose answer was lost is repeated, saves still refused, for this long.</summary>
    public static readonly TimeSpan RetryWithin = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan RetryPause = TimeSpan.FromSeconds(5);

    /// <summary>After a refusal or a lost attempt, the next try waits this long — the cabinet works on the PC meanwhile.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly IRelayCloudClient _cloud;
    private readonly RelayLease _lease;
    private readonly Guid _clinicId;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan> _monotonic;
    private readonly Func<TimeSpan, CancellationToken, Task> _pause;
    private TimeSpan? _answeredSince;
    private TimeSpan? _retryAfter;

    public RelayHandback(
        IRelayCloudClient cloud, RelayLease lease, Guid clinicId, ILogger logger,
        Func<TimeSpan>? monotonic = null, Func<TimeSpan, CancellationToken, Task>? pause = null)
    {
        _cloud = cloud;
        _lease = lease;
        _clinicId = clinicId;
        _logger = logger;
        _monotonic = monotonic ?? SinceStart();
        _pause = pause ?? ((delay, token) => Task.Delay(delay, token));
    }

    /// <summary>A heartbeat was answered: the two minutes of stability count from the first one.</summary>
    public void Answered() => _answeredSince ??= _monotonic();

    /// <summary>The internet has held long enough for the return — or the update before it (EC-11) — to start.</summary>
    public bool IsStable => _answeredSince is { } since && _monotonic() - since >= StableFor;

    /// <summary>A heartbeat went unanswered: stability starts again, and a return interrupted by a restart gives way.</summary>
    public void Unanswered()
    {
        _answeredSince = null;
        if (_lease.IsHandingBack)
        {
            _lease.AbortHandback();
        }
    }

    /// <summary>One step after an answered heartbeat; true when the cloud now holds the cut (phase 1 done).</summary>
    public async Task<bool> StepAsync(
        RelayFollowerState state, RelayHeartbeatAck ack, RelayLocalSide local, CancellationToken cancellationToken)
    {
        var now = _monotonic();
        _answeredSince ??= now;

        if (local.Handback is null)
        {
            return false;
        }

        // EC-11: the return needs the cloud's build — the follower updates this PC first (`UpdateBeforeReturnAsync`).
        // Counted as stuck from now, so an update that never lands still reaches the bell and the vendor (AC-5.9).
        if (ack.UpdateNeeded)
        {
            _lease.MarkReturnBlocked();
            return false;
        }

        if (!_lease.IsHandingBack
            && (now - _answeredSince < StableFor || (_retryAfter is { } after && now < after)))
        {
            return false;
        }

        var id = _lease.BeginHandback();
        var started = _monotonic();
        while (true)
        {
            RelayCallStatus status;
            try
            {
                status = await TryOnceAsync(id, state, local, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Whatever broke here, the cabinet must not be left refusing saves: the attempt is given up.
                _logger.LogError(ex, "PC de secours: reading or sending the cut failed.");
                status = RelayCallStatus.Refused;
            }

            switch (status)
            {
                case RelayCallStatus.Ok:
                    await local.Handback.ForgetCutAsync(_clinicId, cancellationToken);
                    _lease.CompleteReturn(id);
                    _retryAfter = null;
                    _logger.LogInformation("PC de secours: the cut's work is in the cloud (handback {HandbackId}).", id);
                    return true;

                case RelayCallStatus.Unreachable when _monotonic() - started < RetryWithin:
                    await _pause(RetryPause, cancellationToken);
                    continue;

                default:
                    _logger.LogWarning("PC de secours: the return did not land ({Status}); the cabinet keeps working here.", status);
                    _lease.AbortHandback();
                    _retryAfter = _monotonic() + RetryAfterFailure;
                    if (status == RelayCallStatus.Unreachable)
                    {
                        _answeredSince = null;
                    }

                    return false;
            }
        }
    }

    /// <summary>
    /// US-7 / AC-7.3: this PC's cut was overruled by « Reprendre la main ». Its work is sent to be listed « À reprendre »
    /// (files first, so nothing it held is lost), then its log goes and the copy restarts from the cloud. Null while it
    /// waits to try again.
    /// </summary>
    public async Task<RelayFollowerState?> ListOverruledAsync(
        RelayFollowerState state, DateTime cutSinceUtc, RelayLocalSide local, CancellationToken cancellationToken)
    {
        var now = _monotonic();
        if (local.Handback is null || (_retryAfter is { } after && now < after))
        {
            return null;
        }

        try
        {
            var request = await local.Handback.ReadCutAsync(_clinicId, Guid.NewGuid(), state.AppliedSeq, cutSinceUtc, cancellationToken);
            var files = await SendFilesAsync(local, request, cancellationToken);
            var listed = files == RelayCallStatus.Ok
                ? (await _cloud.ListOverruledCutAsync(request, cancellationToken)).Status
                : files;
            if (listed != RelayCallStatus.Ok)
            {
                _logger.LogWarning("PC de secours: the overruled cut could not be listed ({Status}); it stays here.", listed);
                _retryAfter = _monotonic() + RetryAfterFailure;
                return null;
            }

            await local.Handback.ForgetCutAsync(_clinicId, cancellationToken);
            _lease.ForgetUnreturned();
            _logger.LogInformation("PC de secours: the overruled cut is listed « À reprendre » on the cloud; the copy starts afresh.");
            return state with { StoppedReason = null, ReseedNeeded = true, LastError = null };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "PC de secours: reading or sending the overruled cut failed.");
            _retryAfter = _monotonic() + RetryAfterFailure;
            return null;
        }
    }

    private Task<RelayCallStatus> SendFilesAsync(
        RelayLocalSide local, RelayHandbackRequest request, CancellationToken cancellationToken) =>
        SendFilesAsync(_cloud, local, request.Rows, _logger, cancellationToken);

    /// <summary>
    /// The files <paramref name="rows"/> name that the cloud does not hold yet, sent before the rows that name them — for a
    /// return, an overruled cut and a restored cloud's gap alike.
    /// </summary>
    public static async Task<RelayCallStatus> SendFilesAsync(
        IRelayCloudClient cloud, RelayLocalSide local, IReadOnlyList<Application.Common.Interfaces.RelayRow> rows, ILogger logger,
        CancellationToken cancellationToken)
    {
        var keys = local.Handback!.FileKeys(rows);
        if (keys.Count == 0)
        {
            return RelayCallStatus.Ok;
        }

        var missing = await cloud.MissingHandbackFilesAsync(keys, cancellationToken);
        if (!missing.IsOk)
        {
            return missing.Status;
        }

        foreach (var key in missing.Value!)
        {
            Stream content;
            try
            {
                content = await local.Files.DownloadAsync(key, cancellationToken);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
            {
                // A row naming a file this PC never had (it was still copying when the cut came): nothing to send.
                logger.LogWarning("PC de secours: {StorageKey} is named by the cut but not stored here.", key);
                continue;
            }

            await using (content)
            {
                var sent = await cloud.UploadHandbackFileAsync(key, content, cancellationToken);
                if (!sent.IsOk)
                {
                    return sent.Status;
                }
            }
        }

        return RelayCallStatus.Ok;
    }

    private async Task<RelayCallStatus> TryOnceAsync(
        Guid id, RelayFollowerState state, RelayLocalSide local, CancellationToken cancellationToken)
    {
        var request = await local.Handback!.ReadCutAsync(
            _clinicId, id, state.AppliedSeq, _lease.HoldingSinceUtc ?? DateTime.UtcNow, cancellationToken);

        var files = await SendFilesAsync(local, request, cancellationToken);
        if (files != RelayCallStatus.Ok)
        {
            return files;
        }

        return (await _cloud.HandBackAsync(request, cancellationToken)).Status;
    }

    private static Func<TimeSpan> SinceStart()
    {
        var start = Stopwatch.GetTimestamp();
        return () => Stopwatch.GetElapsedTime(start);
    }
}
