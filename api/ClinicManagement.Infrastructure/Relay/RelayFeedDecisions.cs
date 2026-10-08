using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>What to do with a feed batch.</summary>
public enum RelayBatchVerdict
{
    Apply,
    Reseed,
    /// <summary>The cloud is not the history this copy followed — never follow it (D12).</summary>
    Stop,
}

/// <summary>
/// The PC de secours's rules, as pure functions of its state and what the cloud said (<c>clinic-pc-copy</c> D6b, D12,
/// D25). Everything that can lose records is decided here, where it can be tested without a network or a database.
/// </summary>
public static class RelayFeedDecisions
{
    /// <summary>How often the copy is compared with the cloud, table by table (FR-9).</summary>
    public static readonly TimeSpan DigestEvery = TimeSpan.FromHours(1);

    /// <summary>Most batches pulled in one tick, so the heartbeat keeps its rhythm on a long catch-up.</summary>
    public const int MaxBatchesPerTick = 5;

    /// <summary>How long a failed whole copy waits before the next attempt.</summary>
    public static readonly TimeSpan SeedRetryAfterFailure = TimeSpan.FromMinutes(5);

    /// <summary>D19: an admin took the cloud back after this PC's takeover; the cut's work stays here for « À reprendre ».</summary>
    public const string OverruledReason =
        "Le cloud a repris la main pendant la coupure. La copie est arrêtée pour ne rien perdre : ce PC garde ce que le "
        + "cabinet y a enregistré.";

    public const string WentBackReason =
        "Le cloud est revenu à un état antérieur à cette copie (restauration ?). Ce PC garde les données les plus récentes "
        + "et renvoie au cloud ce qu'il a perdu, puis la copie reprend.";

    /// <summary>D12's three tests, in the order the cloud cannot fake: a changed epoch, then the cloud's own verdict.</summary>
    public static RelayBatchVerdict Classify(RelayFollowerState state, RelayFeedBatch batch)
    {
        if (WentBack(state, batch.Epoch, batch.HighWater) || batch.Outcome == RelayFeedOutcome.WentBack)
        {
            return RelayBatchVerdict.Stop;
        }

        return batch.Outcome == RelayFeedOutcome.ReseedRequired ? RelayBatchVerdict.Reseed : RelayBatchVerdict.Apply;
    }

    /// <summary>Whether a snapshot or batch comes from another history, or from behind this copy's position.</summary>
    public static bool WentBack(RelayFollowerState state, string epoch, long highWater) =>
        (state.Epoch is not null && !string.Equals(state.Epoch, epoch, StringComparison.Ordinal))
        || highWater < state.AppliedSeq;

    /// <summary>The state once a batch has applied: the position moves to the batch's high-water.</summary>
    public static RelayFollowerState Applied(RelayFollowerState state, RelayFeedBatch batch) => state with
    {
        Epoch = batch.Epoch,
        AppliedSeq = batch.HighWater,
        HeadFingerprint = batch.HeadFingerprint ?? state.HeadFingerprint,
        LastError = null,
    };

    /// <summary>The state once a whole copy has applied.</summary>
    public static RelayFollowerState Seeded(RelayFollowerState state, RelaySnapshotHeader header, DateTime nowUtc) => state with
    {
        Epoch = header.Epoch,
        AppliedSeq = header.HighWater,
        HeadFingerprint = header.Head,
        RowsSeededAtUtc = state.RowsSeededAtUtc ?? nowUtc,
        ReseedNeeded = false,
        RetrySeedAfterUtc = null,
        LastError = null,
    };

    public static RelayFollowerState Stopped(RelayFollowerState state, string reason) =>
        state with { StoppedReason = reason, LastError = reason };

    /// <summary>Whether the cloud holds changes this copy has not applied.</summary>
    public static bool IsBehind(RelayFollowerState state, long cloudHighWater) => cloudHighWater > state.AppliedSeq;

    /// <summary>
    /// The first-copy percentage (AC-1.9): the rows are a tenth of it, the files the rest — on a dental practice the
    /// radiographs are where the time goes, so a percentage on rows alone would sit at 100 % for most of the copy.
    /// </summary>
    public static int SeedPercent(RelayFollowerState state)
    {
        if (!state.RowsSeeded)
        {
            return 0;
        }

        if (state.FilesTotal <= 0)
        {
            return 100;
        }

        return 10 + (int)(90L * Math.Min(state.FilesCopied, state.FilesTotal) / state.FilesTotal);
    }

    /// <summary>The first copy is complete when the rows are in and every file the rows name is on this PC.</summary>
    public static bool SeedComplete(RelayFollowerState state) =>
        state.RowsSeeded && state.FilesCopied >= state.FilesTotal;

    /// <summary>The hourly check runs only on a copy that is complete and caught up — otherwise every table differs.</summary>
    public static bool DigestDue(RelayFollowerState state, long cloudHighWater, DateTime nowUtc) =>
        SeedComplete(state)
        && !IsBehind(state, cloudHighWater)
        && (state.LastDigestAtUtc is not { } last || nowUtc - last >= DigestEvery);

    /// <summary>The tables whose row count or hash differ, or that one side lacks (D25).</summary>
    public static IReadOnlyList<string> Mismatched(IReadOnlyList<RelayTableDigest> cloud, IReadOnlyList<RelayTableDigest> local)
    {
        var mine = local.ToDictionary(d => d.Table, StringComparer.Ordinal);
        var theirs = cloud.ToDictionary(d => d.Table, StringComparer.Ordinal);
        return theirs.Keys.Union(mine.Keys, StringComparer.Ordinal)
            .Where(table => !theirs.TryGetValue(table, out var c) || !mine.TryGetValue(table, out var m)
                            || c.Count != m.Count || !string.Equals(c.Hash, m.Hash, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>The three fields a snapshot opens with — which history, at which seq, ending on which change row.</summary>
public sealed record RelaySnapshotHeader(string Epoch, long HighWater, string? Head);
