using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The odontogram's plan suggestions, run over the <b>real seeded catalogue</b> rather than a hand-written one.
///
/// <para>⚠️ This is the guard the catalogue additions needed and did not have. <c>ConditionTreatmentsTests</c>
/// proves the ladder is right against a fixed fixture; it cannot notice that <b>adding an act</b> puts a second
/// act on a diagnosis' first rung and so silently removes its pre-fill — the client only fills a plan line when
/// exactly one act holds rank 0. A new row in a seed file is the last place anyone would look for that.</para>
/// </summary>
public class SeededCatalogueSuggestionTests
{
    private static readonly (string Name, ToothCondition? Produces, string? Category)[] Catalogue =
        ProcedureTypeCatalogSeed.CreateFor(Guid.NewGuid())
            .Select(p => (p.Name, p.ResultingCondition, p.Category))
            .ToArray();

    /// <summary>The acts this clinic offers for a diagnosis, and the ranks they hold.</summary>
    private static List<(string Name, int Rank)> Offered(ToothCondition condition) =>
        Catalogue
            .Select(a => (a.Name, Ranks: ConditionTreatments.RanksFor(a.Produces, a.Category)))
            .Where(x => x.Ranks.Any(r => r.Condition == condition))
            .Select(x => (x.Name, Rank: x.Ranks.First(r => r.Condition == condition).Rank))
            .OrderBy(x => x.Rank)
            .ToList();

    /// <summary>What the odontogram pre-fills: the sole act at the best rank, or nothing when several tie.</summary>
    private static string? PreFilled(ToothCondition condition)
    {
        var offered = Offered(condition);
        if (offered.Count == 0) return null;
        var top = offered.Where(o => o.Rank == offered[0].Rank).ToList();
        return top.Count == 1 ? top[0].Name : null;
    }

    // The flagship case: charting a carie must still propose the conservative act, filled in.
    [Fact]
    public void A_Carie_Is_Still_Pre_Filled_With_The_Conservative_Act()
    {
        Assert.Equal("Soin de carie / obturation", PreFilled(ToothCondition.Carie));
    }

    [Fact]
    public void A_Crown_And_A_Bridge_Are_Still_Pre_Filled()
    {
        Assert.Equal("Couronne / bridge (par élément)", PreFilled(ToothCondition.Couronne));
        Assert.Equal("Couronne / bridge (par élément)", PreFilled(ToothCondition.Bridge));
    }

    // The acts that treat nothing on the chart must not turn up as treatments either.
    [Fact]
    public void An_Act_That_Charts_Nothing_Is_Not_Offered_As_A_Restoration()
    {
        foreach (var condition in new[] { ToothCondition.Carie, ToothCondition.Fracture, ToothCondition.Couronne })
        {
            var names = Offered(condition).Select(o => o.Name).ToList();
            Assert.DoesNotContain("Coiffage pulpaire", names);
            Assert.DoesNotContain("Inlay-core (reconstitution corono-radiculaire)", names);
            Assert.DoesNotContain("Couronne provisoire", names);
        }
    }

    // A missing tooth is replaced — never extracted again, and never answered with a bruxism guard or a core.
    [Fact]
    public void A_Missing_Tooth_Is_Offered_Only_Replacements()
    {
        var names = Offered(ToothCondition.ExtraitAbsent).Select(o => o.Name).ToList();

        Assert.DoesNotContain(names, n => n.StartsWith("Extraction"));
        Assert.DoesNotContain("Inlay-core (reconstitution corono-radiculaire)", names);
        Assert.Contains("Implant dentaire", names);
        Assert.Contains("Couronne / bridge (par élément)", names);
        // ⚠️ « Gouttière occlusale » and « Réparation / rebasage » ARE offered here, and that is accepted rather
        // than asserted away: nothing charts a removable prosthesis, so the ladder reaches that discipline by
        // category and cannot tell a denture from the other things filed beside it.
    }

    // Non-vacuity: reflection and lookup guards fail open, and a renamed seed row would empty every list above.
    [Fact]
    public void The_Catalogue_Actually_Reaches_The_Ladders()
    {
        Assert.True(Catalogue.Length >= 30, $"only {Catalogue.Length} seeded acts — the fixture is not the catalogue");
        Assert.True(Offered(ToothCondition.Carie).Count >= 4, "the carie ladder is not being reached at all");
    }
}
