using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The audit ledger's tamper-evidence arithmetic (<c>hosted-security-hardening</c> FR-4.1).
///
/// <para>These cover the three things a walk must catch — an entry edited in place, an entry removed, and an
/// entry's hash erased — plus the two properties that make the chain <i>usable</i> rather than merely correct: a
/// hash that survives the round trip through PostgreSQL's own timestamp precision, and pre-chain history that is
/// counted instead of reported as tampering.</para>
/// </summary>
public class AuditChainTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Range(100, 64).Select(i => (byte)i).ToArray();
    private static readonly Guid ChainKey = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AuditChainEntry Entry(long sequence, string? changedFields = "Status: Issued → Cancelled") =>
        new(
            Id: Guid.Parse($"22222222-2222-2222-2222-{sequence:D12}"),
            ChainKey: ChainKey,
            Sequence: sequence,
            UserId: "local|abc",
            EntityType: "Invoice",
            EntityId: "33333333-3333-3333-3333-333333333333",
            Action: 1,
            ChangedFields: changedFields,
            OccurredAt: new DateTime(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc).AddTicks(sequence),
            IsDeclaredGap: false,
            PreviousHash: null,
            EntryHash: null,
            // Named rather than defaulted: the record deliberately has no default for these two, so that a caller
            // building a chain entry has to decide. The one that did not — SchemaVerificationReader's raw-ADO
            // projection — would otherwise have compiled while making verify-schema report every chained row as
            // altered, which is the one check whose whole value is being believed when it says so.
            ClinicId: null,
            UserEmail: null);

    /// <summary>Chains <paramref name="count"/> entries the way the appender does, and returns them in order.</summary>
    private static List<AuditChainEntry> Chained(int count, byte[]? key = null)
    {
        var chain = new List<AuditChainEntry>();
        string? previous = null;

        for (var i = 1; i <= count; i++)
        {
            var entry = Entry(i) with { PreviousHash = previous };
            var hash = AuditChain.Hash(previous, entry, key ?? Key);
            chain.Add(entry with { EntryHash = hash });
            previous = hash;
        }

        return chain;
    }

    // ---------------------------------------------------------------- Hash

    [Fact]
    public void The_Same_Entry_Always_Hashes_The_Same_Way()
    {
        var entry = Entry(1);

        Assert.Equal(AuditChain.Hash(null, entry, Key), AuditChain.Hash(null, entry, Key));
    }

    [Fact]
    public void A_Different_Key_Produces_A_Different_Hash()
    {
        var entry = Entry(1);

        Assert.NotEqual(AuditChain.Hash(null, entry, Key), AuditChain.Hash(null, entry, OtherKey));
    }

    /// <summary>
    /// The point of a <b>keyed</b> hash: an attacker holding every row can recompute a plain digest chain and
    /// rewrite history undetectably. Without the key they cannot produce the value the walk expects.
    /// </summary>
    [Fact]
    public void The_Hash_Cannot_Be_Reproduced_Without_The_Key()
    {
        var entry = Entry(1);
        var real = AuditChain.Hash(null, entry, Key);

        Assert.All(
            new[] { OtherKey, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(64) },
            guess => Assert.NotEqual(real, AuditChain.Hash(null, entry, guess)));
    }

    /// <summary>
    /// Every covered field moves the hash — <b>derived over the record's own properties</b>, so a field added to
    /// <see cref="AuditChainEntry"/> and forgotten in the canonical form fails here.
    ///
    /// <para>⚠️ <b>It used to say that and not do it.</b> This was an <c>[InlineData]</c> table of ten names while
    /// its own docstring claimed it was derived — so it could only fail on rows somebody remembered to add, which
    /// is exactly how <c>ClinicId</c> came to sit outside the hashed set with this guard green. It now enumerates
    /// the record and <b>throws on an unmapped property</b>, making a new field a compile-time decision.</para>
    /// </summary>
    [Fact]
    public void Changing_Any_Covered_Field_Changes_The_Hash()
    {
        var entry = Entry(1);

        foreach (var property in HashedProperties())
        {
            var altered = property switch
            {
                "Sequence" => entry with { Sequence = 99 },
                "Id" => entry with { Id = Guid.NewGuid() },
                "ChainKey" => entry with { ChainKey = Guid.NewGuid() },
                "UserId" => entry with { UserId = "local|other" },
                "EntityType" => entry with { EntityType = "Patient" },
                "EntityId" => entry with { EntityId = Guid.NewGuid().ToString() },
                "Action" => entry with { Action = 3 },
                "ChangedFields" => entry with { ChangedFields = "Status: Issued → Paid" },
                "OccurredAt" => entry with { OccurredAt = entry.OccurredAt.AddSeconds(1) },
                "IsDeclaredGap" => entry with { IsDeclaredGap = true },
                "ClinicId" => entry with { ClinicId = Guid.NewGuid() },
                "UserEmail" => entry with { UserEmail = "quelquun.dautre@cabinet.tn" },
                _ => throw new ArgumentOutOfRangeException(
                    nameof(property),
                    property,
                    "A new field was added to AuditChainEntry. Decide whether the chain must cover it: if yes, "
                    + "add it to AuditChain.Digest AND map it here; if not, exclude it in HashedProperties with "
                    + "the reason.")
            };

            Assert.NotEqual(AuditChain.Hash(null, entry, Key), AuditChain.Hash(null, altered, Key));
        }
    }

    /// <summary>
    /// The record's fields the chain is supposed to cover: everything but the two that carry the chain itself.
    /// <c>PreviousHash</c> is passed to <c>Hash</c> as its own argument rather than read off the entry, and
    /// <c>EntryHash</c> is what <c>Hash</c> returns — neither is part of an entry's own content.
    /// </summary>
    private static IReadOnlyList<string> HashedProperties() =>
        typeof(AuditChainEntry)
            .GetProperties()
            .Select(p => p.Name)
            .Where(name => name is not ("PreviousHash" or "EntryHash"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Non-vacuity: reflection fails <b>open</b>, and a renamed type would leave the loop above iterating nothing
    /// while reporting success.
    /// </summary>
    [Fact]
    public void The_Field_Scan_Still_Sees_The_Record()
    {
        var properties = HashedProperties();

        Assert.True(properties.Count >= 12, $"Only {properties.Count} hashed properties found — the scan drifted.");
        Assert.Contains("ClinicId", properties);
        Assert.Contains("UserEmail", properties);
    }

    // ── Scheme v2 ──────────────────────────────────────────────────────────────────────────────────────────
    //
    // THE case the scheme exists for. Every read of the journal filters on ClinicId while it was NOT covered by
    // the hash, so `UPDATE "AuditEntries" SET "ClinicId"=NULL WHERE …` removed a row from a cabinet's journal
    // permanently and `verify-schema` went on reporting the chain intact — against precisely the reader the
    // tamper-evidence is sold against, somebody holding full database access.

    [Fact]
    public void Moving_An_Entry_Out_Of_Its_Cabinet_Breaks_The_Chain()
    {
        var chain = ChainedWithTenancy(3);
        var hidden = chain[1] with { ClinicId = null };

        var result = AuditChain.Walk(ChainKey, new[] { chain[0], hidden, chain[2] }, Key);

        Assert.Equal(AuditChainBreak.ContentAltered, result.Break);
        Assert.Equal(hidden.Sequence, result.FirstBrokenSequence);
    }

    [Fact]
    public void Rewriting_The_Author_Breaks_The_Chain()
    {
        var chain = ChainedWithTenancy(3);
        var rewritten = chain[1] with { UserEmail = "quelquun.dautre@cabinet.tn" };

        var result = AuditChain.Walk(ChainKey, new[] { chain[0], rewritten, chain[2] }, Key);

        Assert.Equal(AuditChainBreak.ContentAltered, result.Break);
    }

    [Fact]
    public void A_Hash_Written_Today_Declares_Its_Scheme()
    {
        Assert.StartsWith(
            AuditChain.SchemeV2Prefix, AuditChain.Hash(null, Entry(1), Key), StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠️ <b>The half that makes this shippable without a data migration.</b> Every entry already in a
    /// deployment's ledger is hashed over the v1 form, and re-hashing them all is a migration this change
    /// deliberately avoids. A row whose stored hash carries no scheme marker must still verify — otherwise the
    /// walk reports a deployment's whole history as tampering, which is how a check becomes something an
    /// operator learns to ignore.
    /// </summary>
    [Fact]
    public void An_Entry_Written_Before_This_Change_Still_Verifies()
    {
        var result = AuditChain.Walk(ChainKey, LegacyChained(3), Key);

        Assert.Equal(AuditChainBreak.None, result.Break);
        Assert.Equal(3, result.Checked);
    }

    /// <summary>
    /// And the case that would make the fallback worthless if it were not stated: on a <b>legacy</b> row the
    /// tenancy genuinely is not covered, so nulling it there stays undetectable. That is the pre-existing
    /// exposure, unchanged — recorded here so nobody reads « v2 covers ClinicId » as « the whole ledger does ».
    /// Closing it for old rows is what the rehash migration in
    /// <c>follow-up/security-remediation-progress.md</c> § 2.1 is for.
    /// </summary>
    [Fact]
    public void A_Legacy_Entry_Is_Only_As_Protected_As_It_Ever_Was()
    {
        var legacy = LegacyChained(3);
        var hidden = legacy[1] with { ClinicId = null };

        var result = AuditChain.Walk(ChainKey, new[] { legacy[0], hidden, legacy[2] }, Key);

        Assert.Equal(AuditChainBreak.None, result.Break);
    }

    /// <summary>Chains entries carrying a cabinet, the way the appender does today.</summary>
    private static List<AuditChainEntry> ChainedWithTenancy(int count)
    {
        var chain = new List<AuditChainEntry>();
        string? previous = null;

        for (var i = 1; i <= count; i++)
        {
            var entry = Entry(i) with
            {
                PreviousHash = previous,
                ClinicId = ChainKey,
                UserEmail = "sonia@cabinet.tn",
            };

            var chained = entry with { EntryHash = AuditChain.Hash(previous, entry, Key) };
            chain.Add(chained);
            previous = chained.EntryHash;
        }

        return chain;
    }

    /// <summary>
    /// Chains entries the way the appender did <b>before</b> scheme v2: an unmarked hash computed over a
    /// canonical form with no tenancy in it.
    ///
    /// <para>⚠️ Built through <c>AuditChain.LegacyHash</c> rather than by hashing an entry whose tenancy is set
    /// to null: the v2 form appends two length-prefixed fields whether or not they are empty, so « hash it with
    /// nulls » is a third value that is neither scheme. The rows still carry their <c>ClinicId</c>, exactly as a
    /// real pre-change row does — in the column and not in the hash, which is the whole shape of the old
    /// exposure.</para>
    /// </summary>
    private static List<AuditChainEntry> LegacyChained(int count)
    {
        var chain = new List<AuditChainEntry>();
        string? previous = null;

        for (var i = 1; i <= count; i++)
        {
            var entry = Entry(i) with { PreviousHash = previous, ClinicId = ChainKey };
            var legacyHash = AuditChain.LegacyHash(previous, entry, Key);

            chain.Add(entry with { EntryHash = legacyHash });
            previous = legacyHash;
        }

        return chain;
    }

    /// <summary>
    /// ⚠️ The trap the canonical form exists for. PostgreSQL's <c>timestamptz</c> holds <b>microseconds</b> while
    /// a .NET <c>DateTime</c> holds 100 ns ticks, so an entry hashed before its insert and re-hashed after its
    /// read would differ on the remainder — every single entry reporting as altered, on a check whose whole value
    /// is being believed when it says so.
    /// </summary>
    [Fact]
    public void The_Hash_Survives_The_Round_Trip_Through_The_Columns_Own_Precision()
    {
        var entry = Entry(1) with { OccurredAt = new DateTime(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc).AddTicks(3457) };

        // What PostgreSQL gives back: the same instant truncated to whole microseconds.
        var asStored = entry with
        {
            OccurredAt = new DateTime(
                entry.OccurredAt.Ticks - (entry.OccurredAt.Ticks % (TimeSpan.TicksPerMillisecond / 1000)),
                DateTimeKind.Utc)
        };

        Assert.NotEqual(entry.OccurredAt, asStored.OccurredAt);
        Assert.Equal(AuditChain.Hash(null, entry, Key), AuditChain.Hash(null, asStored, Key));
    }

    /// <summary>
    /// Length-prefixing, proven rather than asserted. With plain separators these two entries would canonicalise
    /// to the same text and hash identically — a forged collision built out of nothing but free text a clinical
    /// aggregate supplies.
    /// </summary>
    [Fact]
    public void A_Crafted_ChangedFields_Cannot_Forge_A_Collision()
    {
        var honest = Entry(1, "Statut") with { EntityType = "Invoice" };
        var crafted = Entry(1, null) with { EntityType = "Invoice|6:Statut" };

        Assert.NotEqual(AuditChain.Hash(null, honest, Key), AuditChain.Hash(null, crafted, Key));
    }

    [Fact]
    public void A_Null_ChangedFields_And_An_Empty_One_Hash_Differently()
    {
        Assert.NotEqual(
            AuditChain.Hash(null, Entry(1, null), Key),
            AuditChain.Hash(null, Entry(1, string.Empty), Key));
    }

    [Fact]
    public void A_Key_Below_The_Floor_Is_Refused()
    {
        Assert.Throws<ArgumentException>(() => AuditChain.Hash(null, Entry(1), new byte[16]));
    }

    // ---------------------------------------------------------------- Walk

    [Fact]
    public void An_Intact_Chain_Walks_Clean()
    {
        var result = AuditChain.Walk(ChainKey, Chained(5), Key);

        Assert.True(result.IsIntact);
        Assert.Equal(5, result.Checked);
        Assert.Equal(0, result.Unchained);
        Assert.Equal(0, result.DeclaredGaps);
    }

    [Fact]
    public void An_Entry_Edited_In_Place_Is_Named()
    {
        var chain = Chained(5);
        chain[2] = chain[2] with { ChangedFields = "Status: Issued → Paid" };

        var result = AuditChain.Walk(ChainKey, chain, Key);

        Assert.Equal(AuditChainBreak.ContentAltered, result.Break);
        Assert.Equal(3, result.FirstBrokenSequence);
        Assert.Equal(chain[2].Id, result.FirstBrokenEntryId);
    }

    [Fact]
    public void A_Removed_Entry_Is_Named()
    {
        var chain = Chained(5);
        chain.RemoveAt(2);

        var result = AuditChain.Walk(ChainKey, chain, Key);

        Assert.Equal(AuditChainBreak.SequenceGap, result.Break);
        Assert.Equal(4, result.FirstBrokenSequence);
    }

    /// <summary>
    /// The removal an attacker would try to cover up: renumber what is left so the sequence reads continuous. The
    /// link to the predecessor is what catches it — and renumbering also changes each entry's own hash, so there
    /// is no version of this that walks clean.
    /// </summary>
    [Fact]
    public void A_Removed_Entry_Is_Still_Named_When_The_Sequence_Is_Closed_Up()
    {
        var chain = Chained(5);
        chain.RemoveAt(2);
        for (var i = 2; i < chain.Count; i++)
        {
            chain[i] = chain[i] with { Sequence = i + 1 };
        }

        var result = AuditChain.Walk(ChainKey, chain, Key);

        Assert.NotEqual(AuditChainBreak.None, result.Break);
    }

    [Fact]
    public void A_Chain_Verified_With_The_Wrong_Key_Reports_A_Break()
    {
        var result = AuditChain.Walk(ChainKey, Chained(3), OtherKey);

        Assert.Equal(AuditChainBreak.ContentAltered, result.Break);
    }

    /// <summary>
    /// Every row written before this feature shipped carries no hash, and no key can cover them retroactively.
    /// Reporting a deployment's whole history as tampering is how a check becomes one nobody reads.
    /// </summary>
    [Fact]
    public void Entries_Predating_The_Chain_Are_Counted_Rather_Than_Refused()
    {
        var history = new[] { Entry(1) with { EntryHash = null }, Entry(2) with { EntryHash = null } };
        var chained = Chained(3).Select((e, i) => e with { Sequence = i + 3 }).ToList();

        // The first chained entry legitimately starts from null: it follows history nothing can link to.
        chained[0] = chained[0] with { PreviousHash = null };
        chained[0] = chained[0] with { EntryHash = AuditChain.Hash(null, chained[0], Key) };
        for (var i = 1; i < chained.Count; i++)
        {
            chained[i] = chained[i] with { PreviousHash = chained[i - 1].EntryHash };
            chained[i] = chained[i] with { EntryHash = AuditChain.Hash(chained[i].PreviousHash, chained[i], Key) };
        }

        var result = AuditChain.Walk(ChainKey, history.Concat(chained), Key);

        Assert.True(result.IsIntact);
        Assert.Equal(2, result.Unchained);
        Assert.Equal(3, result.Checked);
    }

    /// <summary>
    /// The other direction, and the one that is a break: chained history cannot un-chain itself, so an entry whose
    /// hash has been erased <b>after</b> chained ones is exactly what hiding an edit looks like.
    /// </summary>
    [Fact]
    public void An_Unchained_Entry_After_A_Chained_One_Is_A_Break()
    {
        var chain = Chained(4);
        chain[2] = chain[2] with { PreviousHash = null, EntryHash = null };

        var result = AuditChain.Walk(ChainKey, chain, Key);

        Assert.Equal(AuditChainBreak.UnchainedAfterChained, result.Break);
        Assert.Equal(3, result.FirstBrokenSequence);
    }

    /// <summary>
    /// A declared gap is <b>inside</b> the chain, not a hole in it — that is what lets a later walk tell « a gap
    /// we know about » from « a break nobody declared », which is the whole distinction FR-4.1 asks for.
    /// </summary>
    [Fact]
    public void A_Declared_Gap_Is_Counted_And_Is_Not_A_Break()
    {
        var chain = new List<AuditChainEntry>();
        string? previous = null;

        for (var i = 1; i <= 3; i++)
        {
            var entry = Entry(i) with { PreviousHash = previous, IsDeclaredGap = i == 2 };
            var hash = AuditChain.Hash(previous, entry, Key);
            chain.Add(entry with { EntryHash = hash });
            previous = hash;
        }

        var result = AuditChain.Walk(ChainKey, chain, Key);

        Assert.True(result.IsIntact);
        Assert.Equal(1, result.DeclaredGaps);
        Assert.Equal(3, result.Checked);
    }

    /// <summary>A gap cannot be forged either: the flag is covered by the hash.</summary>
    [Fact]
    public void Marking_An_Existing_Entry_As_A_Declared_Gap_Breaks_The_Chain()
    {
        var chain = Chained(3);
        chain[1] = chain[1] with { IsDeclaredGap = true };

        Assert.Equal(AuditChainBreak.ContentAltered, AuditChain.Walk(ChainKey, chain, Key).Break);
    }

    [Fact]
    public void An_Empty_Chain_Walks_Clean()
    {
        var result = AuditChain.Walk(ChainKey, Array.Empty<AuditChainEntry>(), Key);

        Assert.True(result.IsIntact);
        Assert.Equal(0, result.Checked);
    }

    [Fact]
    public void A_Malformed_Stored_Hash_Reads_As_An_Alteration_Rather_Than_Throwing()
    {
        var chain = Chained(2);
        chain[1] = chain[1] with { EntryHash = "not base64 at all!!" };

        Assert.Equal(AuditChainBreak.ContentAltered, AuditChain.Walk(ChainKey, chain, Key).Break);
    }

    [Fact]
    public void Every_Break_Has_Its_Own_French_Sentence()
    {
        var verdicts = Enum.GetValues<AuditChainBreak>().Where(v => v != AuditChainBreak.None).ToList();

        Assert.NotEmpty(verdicts);
        Assert.All(verdicts, v => Assert.NotEqual("rupture non classée", AuditChain.Describe(v)));
        Assert.Equal(verdicts.Count, verdicts.Select(AuditChain.Describe).Distinct().Count());
    }
}
