using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.UnitTests.Features.ProcedureTypes;

/// <summary>
/// The starter catalogue paints an act by its clinical discipline, and that colour is the only thing telling two
/// appointment blocks apart at a glance in the agenda, on the dashboard's act legend and on the act picker's rows.
///
/// <para>
/// So <b>two disciplines sharing a hue is a defect, not a duplicate constant</b> — and it shipped twice:
/// « Esthétique » carried « Orthodontie »'s rose and « Pédodontie » carried « Parodontologie »'s vert, collapsing
/// twelve categories into ten colours. A facette and a séance orthodontique rendered identically for as long as it
/// stood. Nothing failed, nothing logged, and each line of the map is correct read on its own — which is why this
/// is a derived check over the whole catalogue rather than an assertion about any one entry.
/// </para>
/// <para>
/// ⚠️ It now compares <b>families</b>, not hexes. Each act takes a *tone* of its discipline's family, so two acts
/// legitimately differ in hex while belonging to the same discipline — a per-hex uniqueness check would have read
/// that as twelve categories becoming thirty-four and passed while saying nothing.
/// </para>
/// <para>
/// It reads the colours back off the entities <see cref="ProcedureTypeCatalogSeed.CreateFor"/> actually builds,
/// not off the private map, so a row that never reaches a <c>ProcedureType</c> — a category missing from the map
/// and silently taking <c>FallbackColor</c> — is caught by the same test.
/// </para>
/// </summary>
public class CategoryColoursAreDistinctTests
{
    /// <summary>Every seeded act as (discipline, hex, hue family) — the family resolved through the palette.</summary>
    private static List<(string Category, string Hex, string Family)> SeededActs()
    {
        var familyOf = ColorHex.GetPalette()
            .SelectMany(f => f.Tones.Select(t => (t.Hex, f.Key)))
            .ToDictionary(x => x.Hex, x => x.Key, StringComparer.OrdinalIgnoreCase);

        return ProcedureTypeCatalogSeed.CreateFor(Guid.NewGuid())
            .Select(a => (
                Category: a.Category ?? "(sans catégorie)",
                Hex: a.Color.Value,
                // « (hors palette) » rather than a throw: a hex the palette does not carry is exactly the defect
                // this file exists to report, and it must arrive as a named failure, not an exception.
                Family: familyOf.TryGetValue(a.Color.Value, out var f) ? f : "(hors palette)"))
            .ToList();
    }

    [Fact]
    public void Every_Seeded_Category_Has_Its_Own_Hue_Family()
    {
        var familiesByCategory = SeededActs()
            .GroupBy(a => a.Category, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Family).Distinct().ToList());

        // A discipline that spans two families is as wrong as two disciplines sharing one: the hue is what says
        // « this is surgery », and an act painted out of family reads as a different discipline entirely.
        foreach (var (category, families) in familiesByCategory)
        {
            Assert.True(families.Count == 1, $"« {category} » spans {families.Count} hues: {string.Join(", ", families)}");
        }

        var collisions = familiesByCategory
            .GroupBy(pair => pair.Value[0], StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} → {string.Join(" + ", group.Select(p => p.Key).Order())}")
            .ToList();

        Assert.True(
            collisions.Count == 0,
            "each clinical discipline must be its own hue in the agenda; these share one: "
                + string.Join(" · ", collisions));
    }

    /// <summary>
    /// Inside a discipline the acts must actually differ — that is the whole point of taking a tone per act.
    ///
    /// <para>⚠️ A family carries three tones and « Pédodontie » has five acts, so the tones cycle and a fourth act
    /// repeats the first's colour. The bound asserted is therefore « no more than <c>ceil(n / 3)</c> acts on one
    /// hex », not « all distinct » — an all-distinct assertion could only be satisfied by borrowing an unrelated
    /// family, which is precisely what the test above forbids.</para>
    /// </summary>
    [Fact]
    public void Acts_Of_One_Discipline_Do_Not_All_Share_A_Colour()
    {
        var tonesPerFamily = ColorHex.GetPalette().Min(f => f.Tones.Count);
        Assert.True(tonesPerFamily > 1, "the palette carries one tone per family — no per-act distinction is possible");

        foreach (var group in SeededActs().GroupBy(a => a.Category, StringComparer.Ordinal))
        {
            var acts = group.ToList();
            var expectedDistinct = Math.Min(acts.Count, tonesPerFamily);
            var actualDistinct = acts.Select(a => a.Hex).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            Assert.True(
                actualDistinct == expectedDistinct,
                $"« {group.Key} » has {acts.Count} acts on {actualDistinct} colours; the palette allows "
                    + $"{expectedDistinct}");

            var worstShare = acts.GroupBy(a => a.Hex, StringComparer.OrdinalIgnoreCase).Max(g => g.Count());
            Assert.True(
                worstShare <= (int)Math.Ceiling(acts.Count / (double)tonesPerFamily),
                $"« {group.Key} » puts {worstShare} acts on one colour");
        }
    }

    /// <summary>
    /// The count guard. Without it « no collisions found » would also be the verdict on a seed that had stopped
    /// producing rows at all — the failure mode a uniqueness assertion is structurally blind to.
    /// </summary>
    [Fact]
    public void The_Seed_Files_Every_Row_Under_A_Discipline()
    {
        var acts = SeededActs();

        Assert.DoesNotContain("(sans catégorie)", acts.Select(a => a.Category));
        Assert.DoesNotContain("(hors palette)", acts.Select(a => a.Family));
        Assert.Equal(ProcedureTypeCatalogSeed.Rows.Count, acts.Count);
        Assert.Equal(
            ProcedureTypeCatalogSeed.Rows.Select(r => r.Category).Distinct(StringComparer.Ordinal).Count(),
            acts.Select(a => a.Category).Distinct(StringComparer.Ordinal).Count());
    }
}
