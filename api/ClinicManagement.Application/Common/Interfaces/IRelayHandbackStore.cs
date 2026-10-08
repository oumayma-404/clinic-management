using System.Text.Json;
using ClinicManagement.Application.Features.Relay;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>One row of the relay scope: its table (the entity's CLR name) and its key as the change log writes it.</summary>
public readonly record struct RelayRowKey(string Table, string Key);

/// <summary>One change of the cloud's log after the PC's base (D18): who wrote it (the cloud, or an earlier return).</summary>
public sealed record RelayCloudChange(
    long Seq, string Table, string Key, bool Deleted, bool FromRelay, string? IdempotencyKey, DateTime RecordedAtUtc);

/// <summary>
/// The return's row access (<c>clinic-pc-copy</c> D18), in SQL like the copy: the PC reads its cut, the cloud applies it.
/// Every cloud-side member runs inside the caller's transaction, so the rows, the change log, the journal and the release
/// of the fence commit together.
/// </summary>
public interface IRelayHandbackStore
{
    // ---- the PC de secours ---------------------------------------------------------------------------------------

    /// <summary>
    /// Waits for every save already past this PC's gate, then reads the cut in one snapshot: the PC's own log, the current
    /// row of each key, the journal since <paramref name="cutSinceUtc"/>, the sign-in traces and the spent recovery codes.
    /// </summary>
    Task<RelayHandbackRequest> ReadCutAsync(
        Guid clinicId, Guid handbackId, long baseAppliedSeq, DateTime cutSinceUtc, CancellationToken cancellationToken);

    /// <summary>The storage keys the cut's rows name — the files the cloud must hold before the rows.</summary>
    IReadOnlyList<string> FileKeys(IReadOnlyList<RelayRow> rows);

    /// <summary>The cloud holds the cut: this PC's log of it goes.</summary>
    Task ForgetCutAsync(Guid clinicId, CancellationToken cancellationToken);

    // ---- the cloud, inside the caller's transaction --------------------------------------------------------------

    /// <summary>The keys of <paramref name="table"/> a return wrote since <paramref name="sinceUtc"/> — its own transaction.</summary>
    Task<IReadOnlyList<string>> KeysWrittenByReturnAsync(
        Guid clinicId, string table, DateTime sinceUtc, CancellationToken cancellationToken);

    /// <summary>The cloud's changes after <paramref name="afterSeq"/> — what the PC never received.</summary>
    Task<IReadOnlyList<RelayCloudChange>> CloudChangesAfterAsync(Guid clinicId, long afterSeq, CancellationToken cancellationToken);

    /// <summary>The cloud's current rows for <paramref name="keys"/>, as JSON; a key with no row is absent.</summary>
    Task<IReadOnlyDictionary<RelayRowKey, string>> CurrentRowsAsync(
        Guid clinicId, IReadOnlyCollection<RelayRowKey> keys, CancellationToken cancellationToken);

    /// <summary>Who last changed each record, as the journal names them (an e-mail, else an account id).</summary>
    Task<IReadOnlyDictionary<RelayRowKey, string>> AuthorsAsync(
        Guid clinicId, IReadOnlyCollection<RelayRowKey> keys, CancellationToken cancellationToken);

    /// <summary>The rows <paramref name="row"/> points to through its foreign keys (D17's « nothing points to it »).</summary>
    IEnumerable<RelayRowKey> References(string table, JsonElement row);

    /// <summary>
    /// Applies the plan: the PC's rows (the cloud's own settings left as they are, AC-5.7), the duplicates dropped, the
    /// sign-in traces merged (FR-11) — and a change for each in the cloud's log, so the PC's copy follows.
    /// </summary>
    Task ApplyReturnAsync(
        Guid clinicId, RelayHandbackRequest request, RelayHandbackPlan plan, CancellationToken cancellationToken);
}
