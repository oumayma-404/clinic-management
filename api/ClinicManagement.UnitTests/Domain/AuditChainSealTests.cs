using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Truncation at the tip — the one break the chain's own arithmetic cannot see.
///
/// <para><b>Why it is invisible without a seal.</b> Every other check compares an entry against its neighbour:
/// a content edit breaks its own hash, a removed middle row breaks the link and the sequence, an erased hash
/// shows up as unchained-after-chained. Deleting the <b>newest</b> <i>k</i> rows removes no neighbour at all —
/// the shortened chain is internally perfect, and the next append re-links from whatever tip it finds. « Supprime
/// la dernière heure » was therefore the cheapest possible attack on this ledger, and it left nothing behind.</para>
///
/// <para>⚠️ <b>The load-bearing case is <c>A_Truncated_Chain_Still_Verifies_Without_A_Seal</c>.</b> It asserts
/// the hole rather than the fix, and it is what stops somebody deciding the seal is redundant: every other test
/// in this class would still pass if the seal were ignored, because the chain really is intact by every internal
/// measure.</para>
/// </summary>
public class AuditChainSealTests
{
    private static readonly Guid Chain = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(64);
    private static readonly DateTime At = new(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>⚠️ The defect, stated as a passing test. Delete the seal and this is all the product had.</summary>
    [Fact]
    public void A_Truncated_Chain_Still_Verifies_Without_A_Seal()
    {
        var full = Chained(5);
        var truncated = full.Take(3).ToList();

        Assert.True(AuditChain.Walk(Chain, truncated, Key).IsIntact);
    }

    [Fact]
    public void A_Truncated_Chain_Is_Caught_Against_Its_Seal()
    {
        var full = Chained(5);
        var seal = SealOf(full);

        var walk = AuditChain.Walk(Chain, full.Take(3).ToList(), Key, seal);

        Assert.False(walk.IsIntact);
        Assert.Equal(AuditChainBreak.TipTruncated, walk.Break);
        Assert.Equal(seal.Sequence, walk.FirstBrokenSequence);
    }

    /// <summary>
    /// Growing past the seal is the normal state of an append-only ledger: a seal is a point in its history, not
    /// a ceiling. If this ever fails, `verify-schema` reports every healthy deployment as tampered with the day
    /// after it is sealed — which would get the whole check switched off.
    /// </summary>
    [Fact]
    public void A_Chain_That_Has_Grown_Since_Its_Seal_Is_Intact()
    {
        var seal = SealOf(Chained(3));

        Assert.True(AuditChain.Walk(Chain, Chained(9), Key, seal).IsIntact);
    }

    [Fact]
    public void A_Chain_Exactly_At_Its_Seal_Is_Intact()
    {
        var entries = Chained(4);

        Assert.True(AuditChain.Walk(Chain, entries, Key, SealOf(entries)).IsIntact);
    }

    /// <summary>
    /// ⚠️ A length-only check is not enough. Deleting the newest entries and writing replacements up to the same
    /// sequence is the obvious way round it, and every internal check passes on the rebuilt chain — so the seal
    /// records the tip's <b>hash</b>, not merely how far it counted.
    /// </summary>
    [Fact]
    public void A_Chain_Rebuilt_To_The_Same_Length_Is_Still_Caught()
    {
        var seal = SealOf(Chained(4));
        var rebuilt = Chained(4, entityId: "un-autre-dossier");

        var walk = AuditChain.Walk(Chain, rebuilt, Key, seal);

        Assert.Equal(AuditChainBreak.TipTruncated, walk.Break);
    }

    /// <summary>
    /// A more specific break wins. Reporting « tronquée » over an altered row would send an operator looking for
    /// a deletion that never happened, and the row that was actually edited would go unnamed.
    /// </summary>
    [Fact]
    public void An_Altered_Row_Is_Reported_As_Altered_Not_As_Truncation()
    {
        var entries = Chained(4);
        var seal = SealOf(entries);
        var tampered = entries.ToList();
        tampered[1] = tampered[1] with { EntityId = "modifié-après-coup" };

        var walk = AuditChain.Walk(Chain, tampered, Key, seal);

        Assert.Equal(AuditChainBreak.ContentAltered, walk.Break);
    }

    [Fact]
    public void The_Break_Has_A_French_Sentence_Of_Its_Own()
    {
        var described = AuditChain.Describe(AuditChainBreak.TipTruncated);

        Assert.Contains("supprimées", described, StringComparison.Ordinal);
        Assert.NotEqual(AuditChain.Describe(AuditChainBreak.SequenceGap), described);
    }

    private static AuditChainSeal SealOf(IReadOnlyList<AuditChainEntry> entries)
    {
        var tip = entries[^1];
        return new AuditChainSeal(Chain, tip.Sequence, tip.EntryHash!, At);
    }

    private static List<AuditChainEntry> Chained(int count, string entityId = "dossier-1")
    {
        var entries = new List<AuditChainEntry>();
        string? previous = null;

        for (var i = 1; i <= count; i++)
        {
            var entry = new AuditChainEntry(
                Guid.NewGuid(), Chain, i, "user-1", "Patient", entityId, 2, null,
                At.AddMinutes(i), false, previous, null, Guid.Empty, "a@b.tn");

            var hash = AuditChain.Hash(previous, entry, Key);
            entries.Add(entry with { EntryHash = hash });
            previous = hash;
        }

        return entries;
    }
}
