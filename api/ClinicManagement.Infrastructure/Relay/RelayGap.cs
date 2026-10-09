using System.Diagnostics;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// AC-9.4 (<c>clinic-pc-copy</c> EC-21, D12): the cloud went back in time — restored from a backup older than this copy —
/// so this PC holds what it lost: the minutes before the loss, and a cut's work when it held the saves. The PC never
/// follows that cloud and never deletes what it no longer has. It compares, table by table, the rows it holds with the
/// cloud's (per-row hashes, only for tables whose digest differs), sends back the rows the cloud lacks or holds
/// otherwise — files first — and once the cloud has them, copies the cloud afresh. The cloud stays read-only for the
/// cabinet meanwhile, so nothing it numbers can collide with what the PC already gave a patient.
/// </summary>
public sealed class RelayGap
{
    /// <summary>A gap that did not land is tried again after this — the cloud is answering, so not every tick.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private readonly IRelayCloudClient _cloud;
    private readonly Guid _clinicId;
    private readonly ILogger _logger;
    private readonly Func<TimeSpan> _monotonic;
    private TimeSpan? _retryAfter;

    public RelayGap(IRelayCloudClient cloud, Guid clinicId, ILogger logger, Func<TimeSpan>? monotonic = null)
    {
        _cloud = cloud;
        _clinicId = clinicId;
        _logger = logger;
        if (monotonic is null)
        {
            var clock = Stopwatch.StartNew();
            monotonic = () => clock.Elapsed;
        }

        _monotonic = monotonic;
    }

    /// <summary>The copy stopped because the cloud went back: what it holds beyond the cloud is still to be sent.</summary>
    public static bool Pending(RelayFollowerState state) =>
        string.Equals(state.StoppedReason, RelayFeedDecisions.WentBackReason, StringComparison.Ordinal);

    /// <summary>
    /// One attempt. The new state once the cloud holds the gap — the copy follows the cloud's history again, from a fresh
    /// copy — else the state as saved, still <see cref="Pending"/> and carrying its <c>GapId</c> for the next attempt.
    /// </summary>
    public async Task<RelayFollowerState> SendAsync(
        RelayFollowerState state, RelayHeartbeatAck ack, RelayLocalSide local,
        Func<RelayFollowerState, RelayFollowerState> save, CancellationToken cancellationToken)
    {
        if (local.Handback is null || (_retryAfter is { } after && _monotonic() < after))
        {
            return state;
        }

        try
        {
            var gapId = state.GapId ?? Guid.NewGuid();
            if (state.GapId is null)
            {
                state = save(state with { GapId = gapId });
            }

            var rows = await DifferingRowsAsync(local, cancellationToken);
            var status = rows is null
                ? RelayCallStatus.Unreachable
                : await RelayHandback.SendFilesAsync(_cloud, local, rows, _logger, cancellationToken);
            if (status == RelayCallStatus.Ok)
            {
                var sent = await _cloud.ReturnGapAsync(new RelayGapRequest(gapId, rows!), cancellationToken);
                status = sent.Status;
                if (sent.IsOk)
                {
                    _logger.LogWarning("PC de secours: the cloud went back in time; {Rows} row(s) it had lost are back in it "
                                       + "({Applied} applied, {Kept} kept as the cloud changed them since). The copy starts afresh.",
                        rows!.Count, sent.Value!.Applied, sent.Value.Listed);
                    _retryAfter = null;
                    return state with
                    {
                        StoppedReason = null,
                        LastError = null,
                        GapId = null,
                        Epoch = ack.Epoch,
                        AppliedSeq = ack.HighWater,
                        HeadFingerprint = null,
                        ReseedNeeded = true,
                        MismatchTables = null,
                    };
                }
            }

            _logger.LogWarning("PC de secours: what the restored cloud lost could not be sent back ({Status}); this PC keeps it.", status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "PC de secours: reading or sending what the restored cloud lost failed; this PC keeps it.");
        }

        _retryAfter = _monotonic() + RetryAfterFailure;
        return state;
    }

    /// <summary>The rows this PC holds that the cloud lacks or holds otherwise; null when the cloud did not answer.</summary>
    private async Task<IReadOnlyList<RelayRow>?> DifferingRowsAsync(RelayLocalSide local, CancellationToken cancellationToken)
    {
        var cloudDigest = await _cloud.DigestAsync(cancellationToken);
        if (!cloudDigest.IsOk)
        {
            return null;
        }

        var theirs = cloudDigest.Value!.ToDictionary(d => d.Table, StringComparer.Ordinal);
        var keys = new List<RelayRowKey>();
        foreach (var mine in await local.Rows.DigestAsync(_clinicId, cancellationToken))
        {
            if (mine.Count == 0 || RelayHandbackRules.NeverReturned.Contains(mine.Table)
                || (theirs.TryGetValue(mine.Table, out var cloud) && cloud.Count == mine.Count && cloud.Hash == mine.Hash))
            {
                continue;
            }

            var cloudRows = await _cloud.GapHashesAsync(mine.Table, cancellationToken);
            if (!cloudRows.IsOk)
            {
                return null;
            }

            var cloudHashes = cloudRows.Value!.ToDictionary(h => h.Key, h => h.Hash, StringComparer.Ordinal);
            foreach (var (key, hash) in await local.Rows.RowHashesAsync(_clinicId, mine.Table, cancellationToken))
            {
                if (!cloudHashes.TryGetValue(key, out var theirsHash) || theirsHash != hash)
                {
                    keys.Add(new RelayRowKey(mine.Table, key));
                }
            }
        }

        return keys.Count == 0 ? Array.Empty<RelayRow>() : await local.Rows.ReadRowsAsync(_clinicId, keys, cancellationToken);
    }
}
