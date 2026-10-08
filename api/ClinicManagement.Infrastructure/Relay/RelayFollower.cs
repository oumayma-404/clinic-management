using System.IO.Compression;
using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>The PC's own database and disk, as one tick sees them (scoped services, handed in per tick).</summary>
public sealed record RelayLocalSide(IClinicRelayRowStore Rows, IRelayBlobIndex Blobs, IFileStorage Files);

/// <summary>What the PC says about the machine on each heartbeat.</summary>
public sealed record RelayHostReport(IReadOnlyList<string> LanAddresses, string? CertificateFingerprint, long? DiskFreeBytes);

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
    private readonly Func<DateTime> _utcNow;
    private readonly ILogger _logger;
    private readonly Dictionary<string, DateTime> _fileRetryAfter = new(StringComparer.Ordinal);

    public RelayFollower(
        IRelayCloudClient cloud,
        RelayFollowerStateStore stateStore,
        RelayCredentials credentials,
        IUserSecretProtector secrets,
        string build,
        Func<RelayHostReport> host,
        RelayUpdater updater,
        ILogger logger,
        Func<DateTime>? utcNow = null)
    {
        _cloud = cloud;
        _stateStore = stateStore;
        _credentials = credentials;
        _secrets = secrets;
        _build = build;
        _host = host;
        _updater = updater;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
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

        var ack = await _cloud.HeartbeatAsync(Report(state), cancellationToken);
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

        state = Received(state, ack.Value!) with { UpdateNeeded = ack.Value!.UpdateNeeded };

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

        // A stopped copy may hold more than the cloud: it is never updated toward that cloud either — a human decides.
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

    /// <summary>The stand-down from a given state; the state it leaves, or null when the cloud did not confirm it.</summary>
    private async Task<RelayFollowerState?> StandDownAsync(
        RelayFollowerState state, bool isUpdating, CancellationToken cancellationToken)
    {
        // A released PC holds no lease: the cloud fences nothing for it.
        if (state.Released || state.ErasedAtUtc is not null)
        {
            return state;
        }

        for (var round = 0; round < 2; round++)
        {
            var ack = await _cloud.HeartbeatAsync(Report(state, isUpdating, wantsToStandDown: true), cancellationToken);
            if (!ack.IsOk || ack.Value!.Armed)
            {
                _logger.LogWarning("PC de secours: the cloud did not confirm the stand-down ({Status}).", ack.Status);
                return null;
            }

            state = Save(Received(state, ack.Value));
        }

        return state;
    }

    /// <summary>What an ack tells the PC about the lease; kept so the next heartbeat confirms it (D14).</summary>
    private RelayFollowerState Received(RelayFollowerState state, RelayHeartbeatAck ack) =>
        ack.AckSeq <= 0
            ? state
            : state with { LastAckSeq = ack.AckSeq, LastAckArmed = ack.Armed, LastAckReceivedAtUtc = _utcNow() };

    private RelayHeartbeatRequest Report(RelayFollowerState state, bool? isUpdating = null, bool wantsToStandDown = false)
    {
        var host = _host();
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
            ConfirmedAckSeq: state.LastAckSeq,
            ConfirmedAckArmed: state.LastAckArmed,
            WantsToStandDown: wantsToStandDown);
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
