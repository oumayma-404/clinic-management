using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The act catalogue's clinical discipline: <see cref="ProcedureTypeCategories"/>, the entity that stores it, and
/// the seed that had been smuggling it through the description column.
///
/// <para>
/// The load-bearing case here is <c>Normalize</c>. The category is deliberately <b>open</b> text — a clinic may
/// invent « Occlusodontie » — and the only thing standing between that and three spellings of one discipline is
/// this fold. Every other feature in this file (grouping, the filter, the suggestion list) silently assumes acts in
/// one discipline share one string, so a regression here degrades every one of them at once while each continues to
/// look correct in isolation.
/// </para>
/// </summary>
public class ProcedureTypeCategoryTests
{
    private static ProcedureType Act(string? category) =>
        new(
            id: Guid.NewGuid(),
            clinicId: Guid.NewGuid(),
            name: "Traitement de canal",
            defaultDurationMinutes: 60,
            color: ColorHex.FromString("#4F83CC"),
            category: category);

    // Typed instead of picked — the case an open field exists to allow and must survive.
    [Theory]
    [InlineData("endodontie")]
    [InlineData("ENDODONTIE")]
    [InlineData("Endodontie")]
    [InlineData("  Endodontie  ")]
    [InlineData("endodontië")]
    public void Normalize_Folds_Case_Accents_And_Whitespace_Onto_The_Canonical_Spelling(string typed)
    {
        Assert.Equal("Endodontie", ProcedureTypeCategories.Normalize(typed));
    }

    // « Chirurgie/Extraction » is the reason punctuation is dropped from the fold rather than kept: all three of
    // these are written in practice and all three name one discipline.
    [Theory]
    [InlineData("Chirurgie/Extraction")]
    [InlineData("Chirurgie / Extraction")]
    [InlineData("chirurgie-extraction")]
    public void Normalize_Treats_Punctuation_Variants_As_One_Discipline(string typed)
    {
        Assert.Equal("Chirurgie/Extraction", ProcedureTypeCategories.Normalize(typed));
    }

    // The other half of the contract: a label that folds onto nothing is a real category of the clinic's own and
    // must survive verbatim. Silently rewriting or rejecting it would make the field closed in effect.
    [Fact]
    public void Normalize_Keeps_A_Clinic_Authored_Category_Verbatim()
    {
        Assert.Equal("Occlusodontie", ProcedureTypeCategories.Normalize("  Occlusodontie  "));
    }

    // Blank must be ONE value, or « unfiled » becomes two states and every grouping and filter has to know both.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Reads_Blank_As_Unfiled(string? blank)
    {
        Assert.Null(ProcedureTypeCategories.Normalize(blank));
    }

    [Fact]
    public void IsCanonical_Recognises_A_Suggested_Discipline_However_It_Is_Spelled()
    {
        Assert.True(ProcedureTypeCategories.IsCanonical("prothese fixe"));
        Assert.False(ProcedureTypeCategories.IsCanonical("Occlusodontie"));
        Assert.False(ProcedureTypeCategories.IsCanonical(null));
    }

    // The entity must canonicalise too — normalising only at the query layer would leave the write path free to
    // store variants, which is the drift itself.
    [Fact]
    public void The_Entity_Canonicalises_On_Construction_And_On_Update()
    {
        var act = Act("  soins CONSERVATEURS ");
        Assert.Equal("Soins conservateurs", act.Category);

        act.UpdateCategory("endodontie");
        Assert.Equal("Endodontie", act.Category);
    }

    [Fact]
    public void UpdateCategory_With_Blank_Unfiles_The_Act()
    {
        var act = Act("Endodontie");

        act.UpdateCategory("");

        Assert.Null(act.Category);
    }

    /// <summary>
    /// The regression guard for the bug this whole feature corrects.
    ///
    /// <para>
    /// <c>ProcedureTypeCatalogSeed</c> assigned every starter act a discipline and — there being no column for it —
    /// passed it positionally into the constructor's <c>description</c> slot. The two parameters are still adjacent
    /// nullable strings, so a single transposition would restore the defect silently: the acts would look filed,
    /// the descriptions would look written, and only a clinic reading « Endodontie » under « Description » would
    /// ever notice.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_Seeded_Act_Carries_Its_Discipline_In_Category_And_No_Description()
    {
        var seeded = ProcedureTypeCatalogSeed.CreateFor(Guid.NewGuid()).ToList();

        Assert.NotEmpty(seeded);
        Assert.All(seeded, act =>
        {
            Assert.False(string.IsNullOrWhiteSpace(act.Category));
            Assert.Null(act.Description);
        });
    }

    // The seed's rows and the suggestion list are two hand-maintained lists of the same vocabulary. A seeded act
    // filed under a label the suggestions do not offer would appear in the catalogue under a heading nobody can
    // pick — so they are pinned to each other here rather than by hoping they stay in step.
    [Fact]
    public void Every_Seeded_Category_Is_One_Of_The_Suggested_Disciplines()
    {
        var seededCategories = ProcedureTypeCatalogSeed.Rows
            .Select(r => r.Category)
            .Distinct()
            .ToList();

        Assert.All(seededCategories, category =>
            Assert.True(
                ProcedureTypeCategories.IsCanonical(category),
                $"Seed category '{category}' is not in ProcedureTypeCategories.Canonical"));
    }
}
