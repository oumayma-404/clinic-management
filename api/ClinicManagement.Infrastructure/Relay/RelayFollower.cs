using System.IO.Compression;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>The PC's own database and disk, as one tick sees them (scoped services, handed in per tick).</summary>
public sealed record RelayLocalSide(
    IClinicRelayRowStore Rows, IRelayBlobIndex Blobs, IFileStorage Files, IRelayHandbackStore? Handback = null);

/// <summary>What the PC says about the machine on each heartbeat.</summary>
public sealed record RelayHostReport(
    IReadOnlyList<string> LanAddresses,
    string? CertificateFingerprint,
    long? DiskFreeBytes,
    int? HttpsPort = null,
    string? GatewayAddress = null);

/// <summary>
/// The PC de secours's copy, one tick at a time (<c>clinic-pc-copy</c> Part 1 « Copy »): report → first copy or
/// catch-up → files → hourly check — or, while the cloud runs another build, one step of <see cref="RelayUpdater"/>.
/// Every rule that can lose records is in <see cref="RelayFeedDecisions"/>; this class only carries them out and keeps
/// the position on disk after each step that moved it.
/// </summary>
public sealed class RelayFollower
{
    /// <summary>Files fetched per tick, so a first copy of thousands of radiographs never starves the heartbeat.</summary>
    public const int MaxFilesPerTick = 20;

    /// <summary>A file the cloud does not have is asked for again after this — it may be mid-upload.</summary>
    public static readonly TimeSpan MissingFileRetry = TimeSpan.FromHours(1);

    private readonly IRelayCloudClient _cloud;
    private readonly RelayFollowerStateStore _stateStore;
    private readonly RelayCredentials _credentials;
    private readonly IUserSecretProtector _secrets;
    private readonly string _build;
    private readonly Func<RelayHostReport> _host;
    private readonly RelayUpdater _updater;
    private readonly RelayLease _lease;
    private readonly RelayHandback _handback;
    private readonly RelayGap _gap;
    private readonly Func<DateTime> _utcNow;
    private readonly ILogger _logger;
    private readonly Dictionary<string, DateTime> _fileRetryAfter = new(StringComparer.Ordinal);
    private volatile RelayFollowerState? _latest;

    public RelayFollower(
        IRelayCloudClient cloud,
        RelayFollowerStateStore stateStore,
        RelayCredentials credentials,
        IUserSecretProtector secrets,
        string build,
        Func<RelayHostReport> host,
        RelayUpdater updater,
        RelayLease lease,
        ILogger logger,
        Func<DateTime>? utcNow = null,
        RelayHandback? handback = null,
        RelayGap? gap = null)
    {
        _cloud = cloud;
        _stateStore = stateStore;
        _credentials = credentials;
        _secrets = secrets;
        _build = build;
        _host = host;
        _updater = updater;
        _lease = lease;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _handback = handback ?? new RelayHandback(cloud, lease, credentials.ClinicId, logger);
        _gap = gap ?? new RelayGap(cloud, credentials.ClinicId, logger);
    }

    /// <summary>Opens a TOTP secret sealed for this PC and re-protects it under this install's own key ring (D8).</summary>
    public RelayInboundUnwrap Unwrap => new(sealedValue =>
        RelaySecretEnvelope.Open(sealedValue, _credentials.PrivateKey) is { } plain ? _secrets.Protect(plain) : null);

    public async Task<RelayFollowerState> TickAsync(RelayLocalSide local, CancellationToken cancellationToken)
    {
        var state = _stateStore.Load();

        // AC-8.2: an erased PC tells the cloud, however long the cloud was out of reach when it happened. « Unknown »
        // counts as told — the cloud no longer has a PC to record it against.
        if (state.ErasedAtUtc is not null && !state.ErasureReported)
        {
            var told = await _cloud.ReportErasedAsync(cancellationToken);
            if (told.Status is RelayCallStatus.Ok or RelayCallStatus.Released)
            {
                state = Save(state with { ErasureReported = true });
            }

            return state;
        }

        if (state.Released)
        {
            return state;
        }

        if (_lease.IsHolding)
        {
            return await WhileHoldingAsync(state, local, cancellationToken);
        }

        var ack = await HeartbeatAsync(Report(state), cancellationToken);
        ForgetReturnOnceReleased(ack);
        switch (ack.Status)
        {
            case RelayCallStatus.Ok:
                break;
            case RelayCallStatus.Released:
                _logger.LogWarning("The cloud released this PC de secours: {Reason}", ack.Error);
                return Save(state with { Released = true });
            default:
                // Unreachable or refused: nothing about the cloud's history can be concluded from no answer.
                return state;
        }

        state = state with { UpdateNeeded = ack.Value!.UpdateNeeded };

        // [D12] Every heartbeat says which history the cloud is and how far it has got. A copy that already holds
        // rows stops here, on the first answer that is behind it or from another history — not only when the cloud
        // later overtakes it, which a restored cloud may not do for days (found on the first end-to-end run).
        if (state.StoppedReason is null && state.RowsSeeded
            && RelayFeedDecisions.WentBack(state, ack.Value.Epoch, ack.Value.HighWater))
        {
            _logger.LogError("The cloud answered from behind this copy (epoch {Epoch}, high-water {HighWater}, applied "
                             + "{Applied}); the copy stops.", ack.Value.Epoch, ack.Value.HighWater, state.AppliedSeq);
            return Save(RelayFeedDecisions.Stopped(state, RelayFeedDecisions.WentBackReason));
        }

        // US-7: a copy stopped because « Reprendre la main » overruled its cut lists that cut « À reprendre » on the
        // cloud, then starts afresh — the one stop a machine may undo, since nothing it held is lost by it.
        if (state.StoppedReason == RelayFeedDecisions.OverruledReason && _lease.Current.UnreturnedSinceUtc is { } cutSince)
        {
            var listed = await _handback.ListOverruledAsync(state, cutSince, local, cancellationToken);
            return Save(listed ?? state);
        }

        // AC-9.4: a copy stopped because the cloud went back holds what that cloud lost — it sends it back (under the
        // cloud's own build, as a cut's work goes back: EC-11), then copies the cloud afresh. Nothing is deleted here.
        if (RelayGap.Pending(state))
        {
            if (state.UpdateNeeded)
            {
                return Save(await _updater.StepAsync(state, ack.Value.CloudBuild, Save, AnnounceUpdatingAsync, cancellationToken));
            }

            return Save(await _gap.SendAsync(state, ack.Value, local, Save, cancellationToken) ?? state);
        }

        // Any other stopped copy may hold more than the cloud: it is never updated toward that cloud — a human decides.
        if (state.StoppedReason is not null)
        {
            return Save(state);
        }

        // [D10b] The cloud runs another build: the copy waits while this PC fetches and runs the cloud's own installer.
        if (state.UpdateNeeded)
        {
            return Save(await _updater.StepAsync(state, ack.Value.CloudBuild, Save, AnnounceUpdatingAsync, cancellationToken));
        }

        state = await _updater.SettleAsync(state);

        if (!state.RowsSeeded || state.ReseedNeeded)
        {
            if (state.RetrySeedAfterUtc is { } retry && _utcNow() < retry)
            {
                return Save(state);
            }

            state = Save(await SeedAsync(state, local, null, cancellationToken));
        }
        else
        {
            state = await CatchUpAsync(state, local, ack.Value.HighWater, cancellationToken);
        }

        if (state.RowsSeeded && state.StoppedReason is null && !state.Released && !state.UpdateNeeded)
        {
            state = Save(await CopyFilesAsync(state, local, cancellationToken));

            if (RelayFeedDecisions.DigestDue(state, ack.Value.HighWater, _utcNow()))
            {
                state = Save(await CheckAsync(state, local, cancellationToken));
            }
        }

        return Save(state);
    }

    /// <summary>
    /// Stands this PC down before it stops (AC-6.1, D14): one heartbeat asking for a disarm, then one confirming the
    /// disarmed ack — only then does the cloud stop fencing on this PC's silence. True when both were answered disarmed.
    /// </summary>
    public async Task<bool> StandDownAsync(CancellationToken cancellationToken) =>
        await StandDownAsync(_stateStore.Load(), isUpdating: false, cancellationToken) is not null;

    /// <summary>
    /// A heartbeat beside a copy tick that has been busy past <see cref="RelayLease.PulseAfter"/> (a big radiograph, a
    /// re-seed): without it the cloud would fence itself, and then this PC take over, while the internet works. It reports
    /// the position last saved and only records the ack; everything else the answer says waits for the next tick.
    /// </summary>
    public async Task PulseAsync(CancellationToken cancellationToken)
    {
        if (_lease.SinceLastExchange is not { } since || since < RelayLease.PulseAfter)
        {
            return;
        }

        var state = _latest ?? _stateStore.Load();
        if (state.Released || state.ErasedAtUtc is not null)
        {
            return;
        }

        await HeartbeatAsync(Report(state), cancellationToken);
    }

    /// <summary>
    /// This PC holds the cabinet's saves (D13). It says so on every heartbeat — the cloud stays read-only for the cabinet
    /// — and takes nothing from the cloud, whose rows are now older than this copy's: no catch-up, no files and no check.
    /// It updates only to return the cut under the cloud's build (EC-11), never while the internet is still unsteady.
    /// </summary>
    private async Task<RelayFollowerState> WhileHoldingAsync(
        RelayFollowerState state, RelayLocalSide local, CancellationToken cancellationToken)
    {
        var call = await HeartbeatAsync(Report(state), cancellationToken);
        if (call.Status == RelayCallStatus.Released)
        {
            _logger.LogWarning("The cloud released this PC de secours while it held the cabinet's saves: {Reason}", call.Error);
            return Save(state with { Released = true });
        }

        if (call.IsOk && call.Value!.Reclaimed)
        {
            // D19: an admin took the cloud back after this takeover. The copy stops FIRST — a restart between the two
            // then finds a PC still holding, asks again and is told again — and only then does the lease end, the cut's
            // work marked as never returned: no catch-up, re-seed or erase may touch it before « À reprendre ».
            _logger.LogWarning("An administrator took the cloud back during the cut; this PC stops and keeps the cut's work.");
            var stopped = Save(RelayFeedDecisions.Stopped(state, RelayFeedDecisions.OverruledReason));
            _lease.End();
            return stopped;
        }

        if (!call.IsOk)
        {
            _handback.Unanswered();
            return state;
        }

        _handback.Answered();

        // AC-9.4 / EC-21: the cloud came back from a backup older than this copy. What it lost before the cut goes back
        // first — the cut's own work only once the cloud holds it, and nothing of the cut before (no handback meanwhile).
        if (state.RowsSeeded && !RelayGap.Pending(state)
            && RelayFeedDecisions.WentBack(state, call.Value!.Epoch, call.Value.HighWater))
        {
            _logger.LogError("The cloud came back behind this copy during the cut (epoch {Epoch}, high-water {HighWater}, "
                             + "applied {Applied}); what it lost goes back first.", call.Value.Epoch, call.Value.HighWater,
                state.AppliedSeq);
            state = Save(RelayFeedDecisions.Stopped(state, RelayFeedDecisions.WentBackReason));
        }

        if (RelayGap.Pending(state))
        {
            if (call.Value!.UpdateNeeded)
            {
                return await UpdateBeforeReturnAsync(state, call.Value, cancellationToken);
            }

            if (await _gap.SendAsync(state, call.Value, local, Save, cancellationToken) is not { } sent)
            {
                return state;
            }

            state = Save(sent);
        }

        // D18: the internet is back — once it has held for two minutes, the cut goes back to the cloud.
        if (await _handback.StepAsync(state, call.Value!, local, cancellationToken))
        {
            // Phase 2 at once: this PC no longer holds, and says so, so the cloud takes the saves back now.
            ForgetReturnOnceReleased(await HeartbeatAsync(Report(state), cancellationToken));
        }

        return await UpdateBeforeReturnAsync(state, call.Value!, cancellationToken);
    }

    /// <summary>
    /// EC-11: the cloud runs another build, and the cut must go back under the cloud's own. The installer is fetched
    /// while the cabinet keeps working here; once the internet has held for two minutes, saves are refused with
    /// « Retour au cloud en cours » and the update runs. The new build finds this PC still holding and handing back,
    /// and sends the cut at its first answered heartbeat. A refusal or a failed install takes saves again.
    /// </summary>
    private async Task<RelayFollowerState> UpdateBeforeReturnAsync(
        RelayFollowerState state, RelayHeartbeatAck ack, CancellationToken cancellationToken)
    {
        if (!ack.UpdateNeeded)
        {
            return state.UpdateBuild is null ? state : Save(await _updater.SettleAsync(state with { UpdateNeeded = false }));
        }

        state = Save(await _updater.StepAsync(state with { UpdateNeeded = true }, ack.CloudBuild, Save,
            RefuseSavesForUpdateAsync, cancellationToken));
        if (_lease.IsHandingBack && (state.UpdateLaunchedAtUtc is null || state.UpdateError is not null))
        {
            _lease.AbortHandback();
        }

        return state;
    }

    /// <summary>The updater's last step before the install, on a holding PC: no stand-down (it holds the cut) — saves stop.</summary>
    private Task<RelayFollowerState?> RefuseSavesForUpdateAsync(RelayFollowerState state, CancellationToken cancellationToken)
    {
        if (!_handback.IsStable)
        {
            return Task.FromResult<RelayFollowerState?>(null);
        }

        _lease.BeginHandback();
        _logger.LogWarning("PC de secours: saves stop while this PC updates to build {Build} before sending the cut back.",
            state.UpdateBuild);
        return Task.FromResult<RelayFollowerState?>(state);
    }

    /// <summary>D18 phase 2: the cloud says it holds the cabinet's saves again after this PC's handback.</summary>
    private void ForgetReturnOnceReleased(RelayCall<RelayHeartbeatAck> call)
    {
        if (call.IsOk && call.Value!.ReturnReleased && _lease.Current.ReturnedHandbackId is { } returned)
        {
            _lease.ForgetReturned(returned);
        }
    }

    private Task<RelayCall<RelayHeartbeatAck>> HeartbeatAsync(RelayHeartbeatRequest report, CancellationToken cancellationToken) =>
        _lease.ExchangeAsync(bounded => _cloud.HeartbeatAsync(report, bounded), cancellationToken);

    /// <summary>The stand-down from a given state; the state it leaves, or null when the cloud did not confirm it.</summary>
    private async Task<RelayFollowerState?> StandDownAsync(
        RelayFollowerState state, bool isUpdating, CancellationToken cancellationToken)
    {
        // A released PC holds no lease: the cloud fences nothing for it.
        if (state.Released || state.ErasedAtUtc is not null)
        {
            return state;
        }

        // A PC holding the cabinet's saves never stands down: the cut's work is here, and only its return (D18) frees
        // the cloud. Stopping it leaves the cloud read-only, which is the truth.
        if (_lease.IsHolding)
        {
            _logger.LogWarning("PC de secours: stopping while it holds the cabinet's saves; the cloud stays read-only.");
            return null;
        }

        for (var round = 0; round < 2; round++)
        {
            var ack = await HeartbeatAsync(Report(state, isUpdating, wantsToStandDown: true), cancellationToken);
            if (!ack.IsOk || ack.Value!.Armed)
            {
                _logger.LogWarning("PC de secours: the cloud did not confirm the stand-down ({Status}).", ack.Status);
                return null;
            }
        }

        return state;
    }

    private RelayHeartbeatRequest Report(RelayFollowerState state, bool? isUpdating = null, bool wantsToStandDown = false)
    {
        var host = _host();
        var lease = _lease.Current;
        return new RelayHeartbeatRequest(
            state.AppliedSeq,
            RelayFeedDecisions.SeedPercent(state),
            RelayFeedDecisions.SeedComplete(state),
            state.FilesTotal,
            state.FilesCopied,
            host.DiskFreeBytes,
            isUpdating ?? RelayUpdater.ReportsUpdating(state, _build, _utcNow()),
            _build,
            _utcNow(),
            string.Join(",", host.LanAddresses),
            state.MismatchTables,
            state.StoppedReason ?? state.UpdateError ?? state.LastError,
            host.CertificateFingerprint,
            CopyStopped: state.StoppedReason is not null,
            ConfirmedAckSeq: lease.LastAckSeq,
            ConfirmedAckArmed: lease.LastAckArmed,
            WantsToStandDown: wantsToStandDown,
            Holding: lease.HoldingSinceUtc is not null,
            HoldingSinceUtc: lease.HoldingSinceUtc,
            HoldingUnderAckSeq: lease.HoldingUnderAckSeq,
            HttpsPort: host.HttpsPort,
            GatewayAddress: host.GatewayAddress,
            ReturnedHandbackId: lease.ReturnedHandbackId,
            ReturnStuckSinceUtc: lease.HoldingSinceUtc is not null ? lease.ReturnFirstTriedAtUtc : null,
            FollowedEpoch: state.Epoch);
    }

    /// <summary>
    /// Just before the installer stops this service (D10b « disarm first »): stand down, saying « Mise à jour ». The
    /// update waits for the next tick when the cloud did not confirm — a PC the cloud believes armed must not go quiet.
    /// </summary>
    private Task<RelayFollowerState?> AnnounceUpdatingAsync(RelayFollowerState state, CancellationToken cancellationToken) =>
        StandDownAsync(state, isUpdating: true, cancellationToken);

    private async Task<RelayFollowerState> CatchUpAsync(
        RelayFollowerState state, RelayLocalSide local, long cloudHighWater, CancellationToken cancellationToken)
    {
        for (var i = 0; i < RelayFeedDecisions.MaxBatchesPerTick && RelayFeedDecisions.IsBehind(state, cloudHighWater); i++)
        {
            var call = await _cloud.ChangesAsync(state.AppliedSeq, state.HeadFingerprint, cancellationToken);
            if (!call.IsOk)
            {
                return Save(AfterFailedCall(state, call.Status, call.Error));
            }

            var batch = call.Value!;
            switch (RelayFeedDecisions.Classify(state, batch))
            {
                case RelayBatchVerdict.Stop:
                    _logger.LogError("The cloud's history is not the one this copy follows (epoch {Epoch}, high-water {HighWater}, "
                                     + "applied {Applied}); the copy stops.", batch.Epoch, batch.HighWater, state.AppliedSeq);
                    return Save(RelayFeedDecisions.Stopped(state, RelayFeedDecisions.WentBackReason));

                case RelayBatchVerdict.Reseed:
                    return Save(await SeedAsync(state with { ReseedNeeded = true }, local, null, cancellationToken));
            }

            try
            {
                await local.Rows.ApplyBatchAsync(_credentials.ClinicId, batch.Rows, Unwrap, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "A feed batch after seq {After} did not apply; the copy will be taken again.", state.AppliedSeq);
                return Save(state with
                {
                    ReseedNeeded = true,
                    LastError = "Une partie de la copie n'a pas pu être appliquée : la copie complète est reprise.",
                });
            }

            state = Save(RelayFeedDecisions.Applied(state, batch));
            cloudHighWater = Math.Max(cloudHighWater, batch.HighWater);
        }

        return state;
    }

    /// <summary>The whole copy (<paramref name="tables"/> null) or some tables of it — never toward another history (D12).</summary>
    private async Task<RelayFollowerState> SeedAsync(
        RelayFollowerState state, RelayLocalSide local, IReadOnlyCollection<string>? tables, CancellationToken cancellationToken)
    {
        var call = await _cloud.SnapshotAsync(tables, cancellationToken);
        if (!call.IsOk)
        {
            return AfterFailedCall(state, call.Status, call.Error);
        }

        try
        {
            RelaySnapshotHeader header;
            await using (var gzip = OpenSnapshot(call.Value!))
            {
                header = await RelaySnapshotHeaderReader.ReadAsync(gzip, cancellationToken);
            }

            if (RelayFeedDecisions.WentBack(state, header.Epoch, header.HighWater))
            {
                _logger.LogError("A snapshot from another history was refused (epoch {Epoch}, high-water {HighWater}).",
                    header.Epoch, header.HighWater);
                return RelayFeedDecisions.Stopped(state, RelayFeedDecisions.WentBackReason);
            }

            await using (var gzip = OpenSnapshot(call.Value!))
            {
                await local.Rows.ReplaceAsync(_credentials.ClinicId, gzip, tables, Unwrap, cancellationToken);
            }

            // A partial snapshot repairs tables; only a whole one moves the position, or changes to the other tables
            // between the two seqs would be skipped.
            return tables is null ? RelayFeedDecisions.Seeded(state, header, _utcNow()) : state with { LastError = null };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "A snapshot of the cloud clinic could not be applied.");
            return state with
            {
                LastError = "La copie du cabinet n'a pas pu être appliquée sur ce PC. Nouvel essai sous peu.",
                // A whole copy is the heaviest call there is; a failing one is not repeated every tick.
                RetrySeedAfterUtc = tables is null ? _utcNow() + RelayFeedDecisions.SeedRetryAfterFailure : state.RetrySeedAfterUtc,
            };
        }
        finally
        {
            TryDelete(call.Value!);
        }
    }

    private async Task<RelayFollowerState> CopyFilesAsync(
        RelayFollowerState state, RelayLocalSide local, CancellationToken cancellationToken)
    {
        var keys = await local.Blobs.ListKeysAsync(_credentials.ClinicId, cancellationToken);
        var missing = new List<string>();
        foreach (var key in keys)
        {
            if (!await local.Files.ExistsAsync(key, cancellationToken))
            {
                missing.Add(key);
            }
        }

        var now = _utcNow();
        var copied = 0;
        foreach (var key in missing.Where(k => !_fileRetryAfter.TryGetValue(k, out var after) || after <= now).Take(MaxFilesPerTick))
        {
            var call = await _cloud.BlobAsync(key, cancellationToken);
            if (call.Status is RelayCallStatus.NotFound or RelayCallStatus.Refused)
            {
                // The cloud cannot serve this one (gone, or its object store refused): asked again later, and not
                // counted against the first copy, which would otherwise read « en cours » for ever over one file.
                _logger.LogWarning("The cloud could not serve stored object {StorageKey}: {Reason}", key, call.Error);
                _fileRetryAfter[key] = now + MissingFileRetry;
                continue;
            }

            if (!call.IsOk)
            {
                state = AfterFailedCall(state, call.Status, call.Error);
                break;
            }

            try
            {
                // Downloaded whole to a temporary file first, so the key never holds a half-received file that
                // ExistsAsync would then report as copied (AC-1.9: an interrupted copy resumes).
                await using var file = new FileStream(call.Value!, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await local.Files.RestoreAtKeyAsync(file, "application/octet-stream", key, cancellationToken);
                copied++;
                _fileRetryAfter.Remove(key);
            }
            finally
            {
                TryDelete(call.Value!);
            }
        }

        var unavailable = missing.Count(k => _fileRetryAfter.TryGetValue(k, out var after) && after > now);
        return state with { FilesTotal = keys.Count - unavailable, FilesCopied = keys.Count - missing.Count + copied };
    }

    /// <summary>The hourly check (FR-9, D25): repair what differs, then report only what still differs.</summary>
    private async Task<RelayFollowerState> CheckAsync(
        RelayFollowerState state, RelayLocalSide local, CancellationToken cancellationToken)
    {
        var differing = await CompareAsync(local, cancellationToken);
        if (differing is null)
        {
            return state;
        }

        if (differing.Count > 0)
        {
            _logger.LogWarning("The hourly check found {Count} table(s) differing from the cloud: {Tables}",
                differing.Count, string.Join(", ", differing));
            state = await SeedAsync(state, local, differing, cancellationToken);
            if (state.StoppedReason is not null)
            {
                return state;
            }

            // A save landing between the two reads makes a table differ for a moment; only one that differs twice is reported.
            var after = await CompareAsync(local, cancellationToken) ?? differing;
            differing = after.Intersect(differing, StringComparer.Ordinal).ToList();
        }

        return state with { MismatchTables = differing, LastDigestAtUtc = _utcNow() };
    }

    private async Task<IReadOnlyList<string>?> CompareAsync(RelayLocalSide local, CancellationToken cancellationToken)
    {
        var cloud = await _cloud.DigestAsync(cancellationToken);
        if (!cloud.IsOk)
        {
            return null;
        }

        var mine = await local.Rows.DigestAsync(_credentials.ClinicId, cancellationToken);
        return RelayFeedDecisions.Mismatched(cloud.Value!, mine);
    }

    private static RelayFollowerState AfterFailedCall(RelayFollowerState state, RelayCallStatus status, string? error) => status switch
    {
        RelayCallStatus.Released => state with { Released = true },
        RelayCallStatus.UpdateNeeded => state with { UpdateNeeded = true },
        RelayCallStatus.Refused => state with { LastError = error },
        _ => state,
    };

    private RelayFollowerState Save(RelayFollowerState state)
    {
        // The moment the cloud released this PC, whichever call said so — « Copie arrêtée le 06/10 » (AC-8.1).
        if (state.Released && state.ReleasedAtUtc is null)
        {
            state = state with { ReleasedAtUtc = _utcNow() };
        }

        _stateStore.Save(state);
        _latest = state;
        return state;
    }

    private static Stream OpenSnapshot(string path) =>
        new GZipStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true), CompressionMode.Decompress);

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Reads a snapshot's opening fields without parsing its tables (they are written first, D6).</summary>
public static class RelaySnapshotHeaderReader
{
    private const int MaxHeaderBytes = 64 * 1024;

    public static async Task<RelaySnapshotHeader> ReadAsync(Stream snapshot, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxHeaderBytes];
        var length = 0;
        int read;
        while (length < buffer.Length
               && (read = await snapshot.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken)) > 0)
        {
            length += read;
        }

        return Parse(buffer.AsSpan(0, length), isFinalBlock: length < buffer.Length);
    }

    public static RelaySnapshotHeader Parse(ReadOnlySpan<byte> json, bool isFinalBlock)
    {
        var reader = new Utf8JsonReader(json, isFinalBlock, default);
        string? epoch = null, head = null;
        long? highWater = null;

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
            {
                continue;
            }

            var name = reader.GetString();
            if (name == "tables")
            {
                break;
            }

            reader.Read();
            switch (name)
            {
                case "epoch":
                    epoch = reader.GetString();
                    break;
                case "highWater":
                    highWater = reader.GetInt64();
                    break;
                case "head":
                    head = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
            }
        }

        if (epoch is null || highWater is null)
        {
            throw new InvalidDataException("La copie reçue du cloud ne dit pas de quel état elle provient.");
        }

        return new RelaySnapshotHeader(epoch, highWater.Value, head);
    }
}
