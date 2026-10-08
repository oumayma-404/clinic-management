using System.Text.Json;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// The PC de secours's row access (<c>clinic-pc-copy</c> D3, D6, D6b, D12, D25): the cloud reads a clinic's current rows
/// by key, the PC applies them authoritatively. Rows are whole-table JSON, so a value's type never passes through C#.
/// </summary>
public interface IClinicRelayRowStore
{
    /// <summary>Opens capture for a clinic (its change cursor) — done at pairing, before the first snapshot is read.</summary>
    Task EnsureCursorAsync(Guid clinicId, CancellationToken cancellationToken);

    /// <summary>Closes capture for a clinic: its cursor and its log go (retirement, D2).</summary>
    Task DropCursorAsync(Guid clinicId, CancellationToken cancellationToken);

    /// <summary>The cloud's log high-water for a clinic, 0 when it has no cursor.</summary>
    Task<long> HighWaterAsync(Guid clinicId, CancellationToken cancellationToken);

    /// <summary>The identity of this database's history: any restore changes it (D12).</summary>
    Task<string> FeedEpochAsync(CancellationToken cancellationToken);

    /// <summary>Every key changed after <paramref name="after"/>, read in one snapshot up to its high-water (D6b).</summary>
    Task<RelayFeedBatch> ReadChangesAsync(
        Guid clinicId, long after, string? fingerprint, RelayOutboundWrap wrap, CancellationToken cancellationToken);

    /// <summary>The clinic's whole relay scope (or only <paramref name="tables"/>) as one consistent snapshot.</summary>
    Task WriteSnapshotAsync(
        Guid clinicId, IReadOnlyCollection<string>? tables, RelayOutboundWrap wrap, Stream output,
        CancellationToken cancellationToken);

    /// <summary>Per-table row count and hash over the clinic's rows, per-side columns left out (D25).</summary>
    Task<IReadOnlyList<RelayTableDigest>> DigestAsync(Guid clinicId, CancellationToken cancellationToken);

    /// <summary>PC side: applies a feed batch in one transaction — deletes, then upserts in FK order (D6b).</summary>
    Task ApplyBatchAsync(Guid clinicId, IReadOnlyList<RelayRow> rows, RelayInboundUnwrap unwrap, CancellationToken cancellationToken);

    /// <summary>PC side: makes the listed tables (all when null) equal to a snapshot — upsert every row, delete the rest.</summary>
    Task ReplaceAsync(
        Guid clinicId, Stream snapshot, IReadOnlyCollection<string>? onlyTables, RelayInboundUnwrap unwrap,
        CancellationToken cancellationToken);

    /// <summary>The fingerprint of the change row at <paramref name="seq"/>, null when there is none.</summary>
    Task<string?> FingerprintAsync(Guid clinicId, long seq, CancellationToken cancellationToken);

    /// <summary>
    /// AC-9.4, cloud: moves the restore mark of each cursor counted under another history (all clinics when null) to its
    /// current position — the first look after a restore. In the caller's transaction when there is one.
    /// </summary>
    Task MarkEpochAsync(Guid? clinicId, CancellationToken cancellationToken);

    /// <summary>AC-9.4: each row of <paramref name="table"/> by key, hashed without what the cloud keeps for itself.</summary>
    Task<IReadOnlyDictionary<string, string>> RowHashesAsync(Guid clinicId, string table, CancellationToken cancellationToken);

    /// <summary>AC-9.4, PC: the current rows for <paramref name="keys"/>, in one snapshot; a key with no row is absent.</summary>
    Task<IReadOnlyList<RelayRow>> ReadRowsAsync(
        Guid clinicId, IReadOnlyCollection<RelayRowKey> keys, CancellationToken cancellationToken);
}

/// <summary>Cloud side: how secrets leave — wrapped for one PC's public key (D8).</summary>
public sealed record RelayOutboundWrap(string RelayPublicKey);

/// <summary>PC side: how wrapped secrets are opened and re-protected under this install's own key ring.</summary>
public sealed record RelayInboundUnwrap(Func<string, string?> UnwrapToProtected);

public enum RelayFeedOutcome
{
    Ok = 0,
    /// <summary>The cloud's history is not the one the PC followed — never follow it (D12).</summary>
    WentBack = 1,
    /// <summary>Too far behind to send as one batch: re-seed instead (D6b).</summary>
    ReseedRequired = 2,
}

/// <summary>A key's current row, or a tombstone when <see cref="Row"/> is null.</summary>
public sealed record RelayRow(string Table, string Key, JsonElement? Row);

public sealed record RelayFeedBatch(
    string Epoch,
    long After,
    long HighWater,
    string? HeadFingerprint,
    RelayFeedOutcome Outcome,
    IReadOnlyList<RelayRow> Rows);

public sealed record RelayTableDigest(string Table, long Count, string Hash);
