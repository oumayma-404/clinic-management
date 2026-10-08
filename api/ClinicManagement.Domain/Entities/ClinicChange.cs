using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>One key a save touched, in the clinic's change log (D1–D3): the key, never the values — the feed sends the
/// row as it is when read, so a migration cannot strand an old shape and a key changed forty times travels once.</summary>
public class ClinicChange
{
    public Guid ClinicId { get; private set; }

    /// <summary>Per clinic, assigned under the clinic's cursor row lock, so commit order is seq order.</summary>
    public long Seq { get; private set; }

    /// <summary>The EF entity type's CLR name — the relay scope's table name.</summary>
    public string Table { get; private set; } = string.Empty;

    /// <summary>The row's primary key as text, composite keys joined with <see cref="KeySeparator"/>.</summary>
    public string EntityKey { get; private set; } = string.Empty;

    public ClinicChangeOp Op { get; private set; }
    public ClinicChangeOrigin Origin { get; private set; }

    /// <summary>The <c>Idempotency-Key</c> of the request that made this change, when it carried one (D17).</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>The ASCII unit separator: a user id is <c>local|{guid}</c>, so a printable separator would be ambiguous.</summary>
    public const char KeySeparator = '\u001f';

    private ClinicChange() { }

    public ClinicChange(
        Guid clinicId, long seq, string table, string entityKey, ClinicChangeOp op, ClinicChangeOrigin origin,
        string? idempotencyKey, DateTime recordedAtUtc)
    {
        ClinicId = clinicId;
        Seq = seq;
        Table = table;
        EntityKey = entityKey;
        Op = op;
        Origin = origin;
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        RecordedAtUtc = recordedAtUtc;
    }
}

/// <summary>A clinic's change-log counter. Its presence is what turns capture on for the clinic (D2): created at
/// pairing, removed at retirement, so a clinic with no PC de secours pays one no-op UPDATE per save.</summary>
public class ClinicChangeCursor
{
    public Guid ClinicId { get; private set; }
    public long LastSeq { get; private set; }

    /// <summary>
    /// AC-9.4: the database history (<c>FeedEpochAsync</c>) <see cref="EpochFromSeq"/> was counted under. A restore brings
    /// back the old value, so a cursor whose epoch is not the current one was restored: its mark moves to the current
    /// <see cref="LastSeq"/>, and every change after it was made by this cloud since the restore.
    /// </summary>
    public string? Epoch { get; private set; }

    /// <summary>AC-9.4: <see cref="LastSeq"/> when <see cref="Epoch"/> was first seen — the restore point, in this log.</summary>
    public long EpochFromSeq { get; private set; }

    private ClinicChangeCursor() { }

    public ClinicChangeCursor(Guid clinicId)
    {
        ClinicId = clinicId;
    }
}
