using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// One entry as the chain sees it — the covered fields, and the two hashes that link it to its predecessor
/// (<c>hosted-security-hardening</c> FR-4.1).
///
/// <para>A record rather than the entity for <see cref="SubscriptionLedger"/>'s reason: the write path projects
/// the tracked <c>AuditEntry</c> into it, and <c>verify-schema</c> — which reads over raw ADO and builds no
/// entities — projects the same shape out of PostgreSQL. One arithmetic, two readers.</para>
/// </summary>
/// <param name="PreviousHash">
/// The predecessor's <paramref name="EntryHash"/>, or null for the first chained entry of a chain.
/// </param>
/// <param name="EntryHash">
/// This entry's own hash, or <b>null</b> for a row that predates the chain — real history, recorded before this
/// feature shipped, which no key can retroactively cover. See <see cref="AuditChain.Walk"/> on why that is
/// reported rather than treated as a break.
/// </param>
public sealed record AuditChainEntry(
    Guid Id,
    Guid ChainKey,
    long Sequence,
    string UserId,
    string EntityType,
    string EntityId,
    int Action,
    string? ChangedFields,
    DateTime OccurredAt,
    bool IsDeclaredGap,
    string? PreviousHash,
    string? EntryHash);

/// <summary>Why a chain walk stopped, in the vocabulary the report renders.</summary>
public enum AuditChainBreak
{
    /// <summary>Nothing wrong.</summary>
    None = 0,

    /// <summary>The entry's own hash does not match its contents — a row was altered in place.</summary>
    ContentAltered = 1,

    /// <summary>The entry does not point at its predecessor — a row was removed or inserted between them.</summary>
    LinkBroken = 2,

    /// <summary>A sequence number is missing — a row was removed.</summary>
    SequenceGap = 3,

    /// <summary>
    /// An unchained (hash-less) entry appears <b>after</b> a chained one. Chained history cannot un-chain itself,
    /// so this is what erasing a hash to hide an edit looks like.
    /// </summary>
    UnchainedAfterChained = 4
}

/// <summary>What one chain's walk found.</summary>
/// <param name="Unchained">
/// Entries predating the chain. Counted and reported, never drift — they are the ledger's real history.
/// </param>
public sealed record AuditChainWalkResult(
    Guid ChainKey,
    int Checked,
    int Unchained,
    int DeclaredGaps,
    AuditChainBreak Break,
    long? FirstBrokenSequence,
    Guid? FirstBrokenEntryId)
{
    public bool IsIntact => Break == AuditChainBreak.None;
}

/// <summary>
/// The audit ledger's tamper-evidence arithmetic (<c>hosted-security-hardening</c> FR-4.1): each entry carries a
/// value derived from itself <b>and its predecessor</b>, keyed by a secret the database does not hold, so an entry
/// cannot be altered or removed without breaking the sequence.
///
/// <para><b>One arithmetic, called by both sides.</b> The interceptor computes a hash as it appends and
/// <c>verify-schema</c> re-computes it as it walks — never re-expressed in SQL, the
/// <c>subscription-cover-kind-matches-ledger</c> precedent, which calls the real
/// <see cref="SubscriptionLedger.FoldWithSpans"/> for the same reason: a second copy in a language where no
/// compiler checks it against the first is how the check and the thing it checks drift apart.</para>
///
/// <para>⚠️ <b>Keyed, not a plain digest.</b> A bare SHA-256 chain is re-computable by anyone holding the rows, so
/// an attacker with write access to the database rewrites an entry <i>and</i> every hash after it and the walk
/// reads clean. The key lives in configuration (<c>Audit:ChainKey</c>) and deliberately <b>not</b> on the Data
/// Protection ring — Part C re-protects that ring and FR-3.9 makes it the thing a restore may fail to read, so
/// chain verification has to stay independently checkable.</para>
///
/// <para>⚠️ <b>The timestamp is canonicalised to microseconds, and that is load-bearing.</b> PostgreSQL's
/// <c>timestamptz</c> holds microseconds while a .NET <c>DateTime</c> holds 100 ns ticks, so an entry hashed
/// before its insert and re-hashed after its read would differ on the sub-microsecond remainder — every single
/// entry would report as altered, on a check whose whole job is to be believed when it says so.</para>
///
/// <para>⚠️ <b>Every string is length-prefixed.</b> <c>ChangedFields</c> is free text lifted off a clinical
/// aggregate, so a plain separator would let a crafted value shift the field boundaries and forge a collision.
/// Length prefixing makes the encoding injective.</para>
/// </summary>
public static class AuditChain
{
    /// <summary>The smallest key this accepts. Below 32 bytes the HMAC is weaker than the hash it is built on.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// The value <paramref name="entry"/> must carry, given the hash of the entry before it.
    ///
    /// <para><paramref name="entry"/>'s own <c>PreviousHash</c> and <c>EntryHash</c> are <b>not</b> read — the
    /// first is the parameter, and the second is what this returns.</para>
    /// </summary>
    public static string Hash(string? previousHash, AuditChainEntry entry, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length < MinimumKeyBytes)
        {
            throw new ArgumentException(
                $"La clé de chaînage du journal doit faire au moins {MinimumKeyBytes} octets.", nameof(key));
        }

        var canonical = new StringBuilder();
        AppendField(canonical, previousHash);
        AppendField(canonical, entry.ChainKey.ToString("D", CultureInfo.InvariantCulture));
        AppendField(canonical, entry.Sequence.ToString(CultureInfo.InvariantCulture));
        AppendField(canonical, entry.Id.ToString("D", CultureInfo.InvariantCulture));
        AppendField(canonical, entry.UserId);
        AppendField(canonical, entry.EntityType);
        AppendField(canonical, entry.EntityId);
        AppendField(canonical, entry.Action.ToString(CultureInfo.InvariantCulture));
        AppendField(canonical, entry.ChangedFields);
        AppendField(canonical, CanonicalMoment(entry.OccurredAt));
        AppendField(canonical, entry.IsDeclaredGap ? "1" : "0");

        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    /// <summary>
    /// Walks one chain's entries — which must be ordered by <c>Sequence</c> — and reports the first break.
    ///
    /// <para><b>Three things are checked and they catch different attacks.</b> Re-hashing catches an entry edited
    /// in place; the link to the predecessor catches an entry removed or spliced in; the sequence catches a
    /// removal that also rewrote the link. Any one alone leaves a hole.</para>
    ///
    /// <para>⚠️ <b>Entries with no hash are the ledger's pre-chain history, and are counted rather than
    /// refused.</b> Every row written before this feature shipped has none, and no key can cover them
    /// retroactively — reporting a deployment's whole history as tampering would make the check something an
    /// operator learns to ignore. What <i>is</i> a break is an unchained entry appearing <b>after</b> a chained
    /// one (<see cref="AuditChainBreak.UnchainedAfterChained"/>): chained history cannot un-chain itself, so that
    /// is precisely what erasing a hash to hide an edit looks like.</para>
    ///
    /// <para>⚠️ <b>A declared gap is inside the chain, not a hole in it.</b> When an audit write fails the
    /// interceptor records one chained entry saying so, which is what lets a later walk tell « a gap we know
    /// about » from « a break nobody declared » — so it is counted separately and is never itself a break.</para>
    /// </summary>
    public static AuditChainWalkResult Walk(Guid chainKey, IEnumerable<AuditChainEntry> orderedEntries, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(orderedEntries);

        var checkedCount = 0;
        var unchained = 0;
        var declaredGaps = 0;
        AuditChainEntry? previous = null;

        foreach (var entry in orderedEntries)
        {
            if (entry.IsDeclaredGap)
            {
                declaredGaps++;
            }

            if (entry.EntryHash is null)
            {
                if (previous is not null)
                {
                    return Broken(chainKey, checkedCount, unchained, declaredGaps,
                        AuditChainBreak.UnchainedAfterChained, entry);
                }

                unchained++;
                continue;
            }

            if (previous is not null && entry.Sequence != previous.Sequence + 1)
            {
                return Broken(chainKey, checkedCount, unchained, declaredGaps,
                    AuditChainBreak.SequenceGap, entry);
            }

            // Null on both sides is the legitimate start of a chain: either the deployment's first entry, or the
            // first one written after the pre-chain history above.
            if (!string.Equals(entry.PreviousHash, previous?.EntryHash, StringComparison.Ordinal))
            {
                return Broken(chainKey, checkedCount, unchained, declaredGaps,
                    AuditChainBreak.LinkBroken, entry);
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    Decode(entry.EntryHash), Decode(Hash(entry.PreviousHash, entry, key))))
            {
                return Broken(chainKey, checkedCount, unchained, declaredGaps,
                    AuditChainBreak.ContentAltered, entry);
            }

            checkedCount++;
            previous = entry;
        }

        return new AuditChainWalkResult(
            chainKey, checkedCount, unchained, declaredGaps, AuditChainBreak.None, null, null);
    }

    /// <summary>
    /// The French sentence for a verdict, so the console verb and any later reader word it the same way. The
    /// sequence and the entry id are the caller's to append — a break is only actionable if it names the row.
    /// </summary>
    public static string Describe(AuditChainBreak verdict) => verdict switch
    {
        AuditChainBreak.None => "chaîne intacte",
        AuditChainBreak.ContentAltered => "cette entrée a été modifiée après son écriture",
        AuditChainBreak.LinkBroken => "cette entrée ne pointe pas sur celle qui la précède",
        AuditChainBreak.SequenceGap => "un numéro d'ordre manque avant cette entrée",
        AuditChainBreak.UnchainedAfterChained =>
            "cette entrée n'est plus chaînée alors que celles qui la précèdent le sont",
        _ => "rupture non classée"
    };

    private static AuditChainWalkResult Broken(
        Guid chainKey, int checkedCount, int unchained, int declaredGaps,
        AuditChainBreak verdict, AuditChainEntry entry) =>
        new(chainKey, checkedCount, unchained, declaredGaps, verdict, entry.Sequence, entry.Id);

    /// <summary>
    /// Exactly six fractional digits — PostgreSQL's own precision — so a value hashed in memory and one read back
    /// from the column produce the same text. See the ⚠️ on the class.
    /// </summary>
    private static string CanonicalMoment(DateTime moment)
    {
        var utc = moment.Kind switch
        {
            DateTimeKind.Utc => moment,
            DateTimeKind.Local => moment.ToUniversalTime(),
            // `Unspecified` is assumed UTC everywhere in this solution — ApplicationDbContext's own converter
            // makes that true on the way to the column, so assuming anything else here would disagree with it.
            _ => DateTime.SpecifyKind(moment, DateTimeKind.Utc)
        };

        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static void AppendField(StringBuilder builder, string? value)
    {
        // A null and an empty string are different facts about ChangedFields, so they are encoded differently.
        if (value is null)
        {
            builder.Append("-|");
            return;
        }

        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
    }

    /// <summary>
    /// Base64 back to bytes for a constant-time comparison. A malformed stored value is not a match, and is
    /// reported as an alteration rather than throwing — which is what it is.
    /// </summary>
    private static byte[] Decode(string value)
    {
        var buffer = new byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out var written)
            ? buffer[..written]
            : Array.Empty<byte>();
    }
}
