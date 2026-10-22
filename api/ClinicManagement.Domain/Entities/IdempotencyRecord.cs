namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One <c>Idempotency-Key</c> a cabinet's save carried, and the answer it got (<c>clinic-pc-copy</c> D17, FR-6): a save
/// pressed again after its answer was lost is replayed, never recorded twice. Kept 48 h.
///
/// <para>⚠️ <b>Per side, never copied</b>: the cloud and the PC de secours each remember their own. What travels is the
/// key on every <see cref="ClinicChange"/> a save made, which is what the return compares (D18).</para>
/// <para>Written and read with raw SQL by the API's middleware — never through a save, so it touches no change log, no
/// audit row and no fence.</para>
/// </summary>
public class IdempotencyRecord
{
    public const int MaxKeyLength = 100;
    public const int MaxFingerprintLength = 400;
    public static readonly TimeSpan Retention = TimeSpan.FromHours(48);

    /// <summary>An answer still being produced older than this is a request that died; its key may be claimed again.</summary>
    public static readonly TimeSpan InProgressTimeout = TimeSpan.FromMinutes(2);

    public Guid ClinicId { get; private set; }
    public string Key { get; private set; } = string.Empty;

    /// <summary>The method and path the key was first used with — the same key on another request is a client bug.</summary>
    public string Fingerprint { get; private set; } = string.Empty;

    public IdempotencyRecordState State { get; private set; }
    public int? StatusCode { get; private set; }
    public string? ContentType { get; private set; }
    public string? Body { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private IdempotencyRecord() { }

    /// <summary>A key is a client-generated token: letters, digits, <c>-</c> and <c>_</c>, 8 to 100 of them.</summary>
    public static bool IsValidKey(string? key) =>
        key is { Length: >= 8 and <= MaxKeyLength } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

public enum IdempotencyRecordState
{
    InProgress = 0,
    Completed = 1,
}
