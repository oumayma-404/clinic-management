using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.UnitTests.Features.ProcedureTypes;

/// <summary>
/// The starter catalogue paints an act by its clinical discipline, and that colour is the only thing telling two
/// appointment blocks apart at a glance in the agenda, on the dashboard's act legend and on the act picker's rows.
///
/// <para>
/// So <b>two disciplines sharing a hex is a defect, not a duplicate constant</b> — and it shipped twice:
/// « Esthétique » carried « Orthodontie »'s <c>#FB7185</c> and « Pédodontie » carried « Parodontologie »'s
/// <c>#6BAA75</c>, collapsing twelve categories into ten colours. A facette and a séance orthodontique rendered
/// identically for as long as it stood. Nothing failed, nothing logged, and each line of the map is correct read
/// on its own — which is why this is a derived check over the whole map rather than an assertion about any one
/// entry.
/// </para>
/// <para>
/// It reads the colours back off the entities <see cref="ProcedureTypeCatalogSeed.CreateFor"/> actually builds,
/// not off the private map, so a row that never reaches a <c>ProcedureType</c> — a category missing from the map
/// and silently taking <c>FallbackColor</c> — is caught by the same test.
/// </para>
/// </summary>
public class CategoryColoursAreDistinctTests
{
    private static IReadOnlyDictionary<string, string> SeededColourByCategory()
    {
        var clinicId = Guid.NewGuid();
        var byCategory = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var act in ProcedureTypeCatalogSeed.CreateFor(clinicId))
        {
            // Every seeded row carries its discipline since AddProcedureTypeCategory; a null here would mean the
            // seed stopped filing its own acts, which the assertion below reports rather than hiding in a throw.
            var category = act.Category ?? "(sans catégorie)";
            byCategory[category] = act.Color.Value;
        }

        return byCategory;
    }

    [Fact]
    public void Every_Seeded_Category_Has_Its_Own_Colour()
    {
        var byCategory = SeededColourByCategory();

        var collisions = byCategory
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} → {string.Join(" + ", group.Select(p => p.Key).Order())}")
            .ToList();

        Assert.True(
            collisions.Count == 0,
            "each clinical discipline must be its own colour in the agenda; these share one: "
                + string.Join(" · ", collisions));
    }

    /// <summary>
    /// The count guard. Without it « no collisions found » would also be the verdict on a seed that had stopped
    /// producing rows at all — the failure mode a uniqueness assertion is structurally blind to.
    /// </summary>
    [Fact]
    public void The_Seed_Files_Every_Row_Under_A_Discipline()
    {
        var byCategory = SeededColourByCategory();

        Assert.DoesNotContain("(sans catégorie)", byCategory.Keys);
        Assert.Equal(
            ProcedureTypeCatalogSeed.Rows.Select(r => r.Category).Distinct(StringComparer.Ordinal).Count(),
            byCategory.Count);
    }

    /// <summary>
    /// A colour the curated palette does not accept cannot be saved back through the act form, so a seeded act
    /// would be editable in every field but its own colour. <see cref="ProcedureType"/>'s ctor already refuses
    /// one, which makes this a guard on the map staying inside the palette as both grow.
    /// </summary>
    [Fact]
    public void Every_Seeded_Colour_Is_In_The_Curated_Palette()
    {
        foreach (var (category, hex) in SeededColourByCategory())
        {
            Assert.True(ColorHex.IsValid(hex), $"« {category} » is painted {hex}, which the palette does not offer");
        }
    }
}
