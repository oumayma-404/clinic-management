using ClinicManagement.Infrastructure.Persistence;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Seed-integrity guard for the DB-backed CNAM catalog (FR-5.1/5.2). Replaces the old
/// <c>CnamNomenclatureProviderTests</c>: the curated data moved from the in-code provider to the
/// <see cref="CnamCatalogSeed"/> single-source-of-truth that the <c>AddCnamCatalog</c> migration inserts.
/// These assertions protect the contract the editor's indicative estimate relies on — every entry's
/// lettre clé must be one the VLC set values, and every coefficient must be positive.
/// </summary>
public class CnamCatalogSeedTests
{
    private static readonly HashSet<string> ExpectedCategories = new()
    {
        CnamCatalogSeed.Consultation, CnamCatalogSeed.SoinsConservateurs, CnamCatalogSeed.ChirurgieExtraction,
        CnamCatalogSeed.Prothese, CnamCatalogSeed.Radiologie,
    };

    [Fact]
    public void Seed_Is_A_NonEmpty_Catalogue() // [FR-5.1]
    {
        Assert.NotEmpty(CnamCatalogSeed.Entries);
        Assert.NotEmpty(CnamCatalogSeed.LetterValues);
    }

    [Fact]
    public void Seed_Covers_Every_Category() // [FR-5.1]
    {
        var categories = CnamCatalogSeed.Entries.Select(e => e.Category).ToHashSet();
        Assert.Equal(ExpectedCategories, categories);
    }

    [Fact]
    public void Every_Entry_Has_Required_Fields() // [FR-5.1]
    {
        Assert.All(CnamCatalogSeed.Entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.CodeActe));
            Assert.False(string.IsNullOrWhiteSpace(entry.DesignationFr));
            Assert.Contains(entry.Category, ExpectedCategories);
        });
    }

    [Fact]
    public void Every_Entry_Uses_A_Known_Lettre_Cle_And_Positive_Coefficient() // [FR-5.1] guards the estimate contract
    {
        var knownCles = CnamCatalogSeed.LetterValues.Select(v => v.LettreCle).ToHashSet();
        Assert.All(CnamCatalogSeed.Entries, entry =>
        {
            Assert.Contains(entry.LettreCle, knownCles);
            Assert.True(entry.Coefficient > 0, $"Coefficient must be positive for {entry.CodeActe}");
        });
    }

    [Fact]
    public void Code_Acte_Values_Are_Unique() // [FR-5.1] a stable unique key
    {
        var distinct = CnamCatalogSeed.Entries.Select(e => e.CodeActe).Distinct().Count();
        Assert.Equal(CnamCatalogSeed.Entries.Count, distinct);
    }

    [Fact]
    public void Seed_Ids_Are_Unique_And_Deterministic() // [FR-5.1] stable ids across machines/regenerations
    {
        var ids = CnamCatalogSeed.Entries.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        // Deterministic: recomputing the id from the same key yields the same GUID.
        var first = CnamCatalogSeed.Entries[0];
        Assert.Equal(first.Id, CnamCatalogSeed.DeterministicGuid($"cnam-entry:{first.CodeActe}"));
    }

    [Fact]
    public void Vlc_Values_Are_NonNegative_And_Keys_Unique() // [FR-5.2]
    {
        Assert.All(CnamCatalogSeed.LetterValues, v => Assert.True(v.Value >= 0));
        var keys = CnamCatalogSeed.LetterValues.Select(v => v.LettreCle).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
