using ClinicManagement.Infrastructure.Persistence;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Seed-integrity guard for the DB-backed medication catalog. The <c>AddMedicationCatalog</c> migration
/// inserts these rows from the <see cref="MedicationCatalogSeed"/> single-source-of-truth, so these
/// assertions protect the starter catalog contract: every medication has a brand + at least one DCI
/// molecule, ids are unique + deterministic, and combination products carry more than one molecule.
/// </summary>
public class MedicationCatalogSeedTests
{
    [Fact]
    public void Seed_Is_NonEmpty()
    {
        Assert.NotEmpty(MedicationCatalogSeed.Medications);
        Assert.NotEmpty(MedicationCatalogSeed.Ingredients);
    }

    [Fact]
    public void Every_Medication_Has_Brand_And_At_Least_One_Dci() // [AC-1]
    {
        Assert.All(MedicationCatalogSeed.Medications, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.BrandName));
            Assert.NotEmpty(m.Dcis);
            Assert.All(m.Dcis, d => Assert.False(string.IsNullOrWhiteSpace(d)));
        });
    }

    [Fact]
    public void Dcis_Within_A_Medication_Are_Distinct_Case_Insensitively()
    {
        Assert.All(MedicationCatalogSeed.Medications, m =>
        {
            var distinct = m.Dcis.Select(d => d.Trim().ToLowerInvariant()).Distinct().Count();
            Assert.Equal(m.Dcis.Count, distinct);
        });
    }

    [Fact]
    public void At_Least_One_Combination_Product_Has_Multiple_Molecules() // [AC-1] combination drugs
    {
        Assert.Contains(MedicationCatalogSeed.Medications, m => m.Dcis.Count >= 2);
    }

    [Fact]
    public void Ingredients_Belong_To_Seeded_Medications_And_Count_Matches()
    {
        var medIds = MedicationCatalogSeed.Medications.Select(m => m.Id).ToHashSet();
        Assert.All(MedicationCatalogSeed.Ingredients, i => Assert.Contains(i.MedicationId, medIds));

        var expected = MedicationCatalogSeed.Medications.Sum(m => m.Dcis.Count);
        Assert.Equal(expected, MedicationCatalogSeed.Ingredients.Count);
    }

    [Fact]
    public void Medication_Ids_Are_Unique()
    {
        var ids = MedicationCatalogSeed.Medications.Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Ingredient_Ids_Are_Unique_And_Deterministic() // stable ids across machines / regenerations
    {
        var ids = MedicationCatalogSeed.Ingredients.Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        var first = MedicationCatalogSeed.Ingredients[0];
        Assert.Equal(first.Id, MedicationCatalogSeed.DeterministicGuid($"medication-dci:{first.MedicationId}:{first.Dci}"));
    }

    [Fact]
    public void DeterministicGuid_Is_Pure()
    {
        Assert.Equal(MedicationCatalogSeed.DeterministicGuid("x"), MedicationCatalogSeed.DeterministicGuid("x"));
        Assert.NotEqual(MedicationCatalogSeed.DeterministicGuid("x"), MedicationCatalogSeed.DeterministicGuid("y"));
    }
}
