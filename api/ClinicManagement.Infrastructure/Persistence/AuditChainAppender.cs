using System.Security.Cryptography;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Puts new <see cref="AuditEntry"/> rows in their chain: per chain, under the chain's own advisory lock, read
/// the tip, assign sequences and hashes (<c>hosted-security-hardening</c> FR-4.1 step 4).
///
/// <para>⚠️ <b>The caller must already be inside a transaction, and that is the whole point.</b>
/// <c>pg_advisory_xact_lock</c> releases at the end of the transaction it was taken in — so a lock taken as a
/// bare statement outside one is released the instant that statement's own implicit transaction commits, and
/// serialises <b>nothing</b>: two concurrent saves in one clinic then read the same predecessor and compute the
/// same <c>PreviousHash</c>. This is <c>MigrationLock</c>'s documented lesson arriving from the other side —
/// there the <c>xact</c> variant is wrong because the migration commits part way through; here it is right
/// <i>provided</i> the transaction spans the whole append. <see cref="ApplicationDbContext.SaveChangesAsync"/>
/// is what guarantees that, by opening one when the caller has none.</para>
///
/// <para>⚠️ <b>Chains are locked in ascending key order.</b> One save can legitimately touch two chains (a
/// console verb mutating a cabinet's row alongside an unattributed one), and two appenders taking the same pair
/// in opposite orders is a deadlock. Sorting makes the order total, so it cannot happen.</para>
///
/// <para>⚠️ <b>The unique <c>(ChainKey, Sequence)</c> index stays, and is not made redundant by this lock.</b>
/// The lock is what stops ordinary concurrency producing declared gaps; the index is what makes a missed or
/// mis-scoped lock impossible to hide — a second appender that skipped the lock collides on insert instead of
/// quietly committing a duplicate sequence.</para>
/// </summary>
public static class AuditChainAppender
{
    /// <summary>
    /// The advisory-lock class id. The two-int form occupies a <b>different key space</b> from the single-bigint
    /// form <c>MigrationLock</c> uses, so an audit append and a startup migration can never contend by accident
    /// however their keys happen to collide numerically.
    /// </summary>
    private const int LockClassId = 5314;

    /// <summary>
    /// Chains every unchained row in <paramref name="rows"/>. Returns how many it chained.
    ///
    /// <para>Rows already carrying a sequence are skipped, so a save that stages audit rows twice — a post-commit
    /// side effect saving again on the same context — cannot re-chain what it already wrote.</para>
    /// </summary>
    public static async Task<int> AssignAsync(
        ApplicationDbContext context,
        IReadOnlyList<AuditEntry> rows,
        byte[] key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rows);

        var pending = rows.Where(r => r.Sequence == 0).ToList();
        if (pending.Count == 0)
        {
            return 0;
        }

        foreach (var chainKey in pending.Select(r => r.ChainKey).Distinct().OrderBy(k => k))
        {
            await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0}, {1})",
                new object[] { LockClassId, ObjectId(chainKey) },
                cancellationToken);

            // The tip as the database has it. `Sequence > 0` skips the rows that predate the chain — they carry
            // no hash, so the first chained row after them legitimately starts from null.
            var tip = await context.AuditEntries
                .Where(a => a.ChainKey == chainKey && a.Sequence > 0)
                .OrderByDescending(a => a.Sequence)
                .Select(a => new { a.Sequence, a.EntryHash })
                .FirstOrDefaultAsync(cancellationToken);

            var sequence = tip?.Sequence ?? 0;
            var previousHash = tip?.EntryHash;

            foreach (var row in pending.Where(r => r.ChainKey == chainKey))
            {
                sequence++;
                row.Chain(sequence, previousHash, AuditChain.Hash(previousHash, WithSequence(row, sequence), key));
                previousHash = row.EntryHash;
            }
        }

        return pending.Count;
    }

    /// <summary>
    /// The projection the hash covers, with the sequence this row is about to be given. <c>Chain</c> cannot be
    /// called first — it needs the hash — so the sequence is substituted here rather than the entity being
    /// mutated twice.
    /// </summary>
    private static AuditChainEntry WithSequence(AuditEntry row, long sequence) =>
        row.ToChainEntry() with { Sequence = sequence };

    /// <summary>
    /// A stable 32-bit id for a chain. Derived from a hash rather than from the GUID's own bytes so two chains
    /// that differ only in a low byte do not land adjacent; a collision costs two unrelated cabinets a moment of
    /// serialisation and nothing else.
    /// </summary>
    private static int ObjectId(Guid chainKey) =>
        BitConverter.ToInt32(SHA256.HashData(chainKey.ToByteArray()), 0);
}
