using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The clinical claim « what act treats what diagnosis », and the three ways it was wrong before it existed.
/// </summary>
public class ConditionTreatmentsTests
{
    /// <summary>The seeded catalogue, as (resulting condition, category) — all any selector can read.</summary>
    private static readonly (string Name, ToothCondition? Produces, string Category)[] Catalogue =
    [
        ("Soin de carie / obturation", ToothCondition.Obturation, "Soins conservateurs"),
        ("Traitement de canal (dévitalisation)", ToothCondition.TraitementDeCanal, "Endodontie"),
        ("Couronne / bridge (par élément)", ToothCondition.Couronne, "Prothèse fixe"),
        ("Implant dentaire", ToothCondition.Implant, "Implantologie"),
        ("Extraction simple", ToothCondition.ExtraitAbsent, "Chirurgie/Extraction"),
        ("Extraction chirurgicale (sagesse / dent incluse)", ToothCondition.ExtraitAbsent, "Chirurgie/Extraction"),
        ("Détartrage", null, "Parodontologie"),
        ("Traitement parodontal (surfaçage / curetage)", null, "Parodontologie"),
        ("Prothèse amovible (partielle / complète)", null, "Prothèse amovible"),
        ("Radiographie panoramique", null, "Radiologie"),
    ];

    /// <summary>The acts this clinic would offer for a diagnosis, best first — what the odontogram builds.</summary>
    private static List<(string Name, int Rank)> Offered(ToothCondition condition) =>
        Catalogue
            .Select(a => (a.Name, Ranks: ConditionTreatments.RanksFor(a.Produces, a.Category)))
            .Where(x => x.Ranks.Any(r => r.Condition == condition))
            .Select(x => (x.Name, Rank: x.Ranks.First(r => r.Condition == condition).Rank))
            .OrderBy(x => x.Rank)
            .ToList();

    // ── the reported gap ────────────────────────────────────────────────────────────────────────────────────
    // A carie is a pathology, so no act ends in it and inverting `ResultingCondition` offered nothing at all.
    [Fact]
    public void A_Carie_Offers_Acts_Least_Invasive_First()
    {
        var offered = Offered(ToothCondition.Carie);

        Assert.Equal(
            ["Soin de carie / obturation", "Traitement de canal (dévitalisation)", "Couronne / bridge (par élément)"],
            offered.Take(3).Select(o => o.Name));
        Assert.Equal(0, offered[0].Rank);
        Assert.Contains(offered, o => o.Name.StartsWith("Extraction"));
    }

    // Ranking is the whole value of the order: rank 0 is what a plan pre-fills, so an extraction must never
    // outrank a restoration on a tooth that can still be saved.
    [Fact]
    public void No_Extraction_Ever_Outranks_A_Restoration_For_A_Restorable_Tooth()
    {
        foreach (var condition in new[]
                 {
                     ToothCondition.Carie, ToothCondition.Fracture, ToothCondition.RestaurationDefectueuse,
                 })
        {
            var offered = Offered(condition);
            var restore = offered.First(o => o.Name.StartsWith("Soin de carie")).Rank;
            var extract = offered.First(o => o.Name.StartsWith("Extraction")).Rank;
            Assert.True(restore < extract, $"{condition}: extraction outranks restoration");
        }
    }

    // ── defect 2: a Bridge diagnosis produced a blank, costless plan line ───────────────────────────────────
    // Nothing in the catalogue leaves `Bridge` behind — « Couronne / bridge » is filed under Couronne — so the
    // old inversion found no act for the one shape it was supposed to handle.
    [Fact]
    public void A_Bridge_Diagnosis_Resolves_To_The_Fixed_Prosthesis_Act()
    {
        Assert.Contains(Offered(ToothCondition.Bridge), o => o.Name == "Couronne / bridge (par élément)");
    }

    // ── defect 1, in its worst form ─────────────────────────────────────────────────────────────────────────
    // Inverting `ResultingCondition` answered « Extrait / Absent » with an EXTRACTION act, i.e. proposed pulling
    // a tooth that is already gone. A missing tooth is replaced.
    [Fact]
    public void A_Missing_Tooth_Is_Replaced_Never_Extracted_Again()
    {
        var offered = Offered(ToothCondition.ExtraitAbsent);

        Assert.DoesNotContain(offered, o => o.Name.StartsWith("Extraction"));
        Assert.Equal("Implant dentaire", offered[0].Name);
        Assert.Contains(offered, o => o.Name.StartsWith("Prothèse amovible"));
    }

    // An act with no resulting condition at all is reachable only by its discipline — and périodontal care is
    // the case that needs it, since nothing it does shows up on an odontogram.
    [Fact]
    public void Periodontal_Disease_Is_Matched_By_Discipline_Because_Its_Acts_Chart_Nothing()
    {
        var offered = Offered(ToothCondition.MaladieParodontale);

        Assert.Equal(2, offered.Count);
        Assert.All(offered, o => Assert.True(o.Name is "Détartrage" or "Traitement parodontal (surfaçage / curetage)"));
    }

    // ⚠️ Ambiguity is surfaced, not resolved. Two extraction acts sit at rank 0 for a retained root, and picking
    // between a simple and a surgical one is a judgement about access — the alphabetical accident that used to
    // decide it quoted 200 DT where 60 DT was meant.
    [Fact]
    public void A_Retained_Root_Offers_Both_Extractions_At_The_Same_Rank()
    {
        var offered = Offered(ToothCondition.RacineResiduelle);

        Assert.Equal(2, offered.Count);
        Assert.All(offered, o => Assert.Equal(0, o.Rank));
    }

    // ── what counts as work to do ───────────────────────────────────────────────────────────────────────────
    [Fact]
    public void An_Impacted_Or_Missing_Tooth_Is_A_Finding_Not_Outstanding_Work()
    {
        Assert.DoesNotContain(ToothCondition.DentIncluse, ConditionTreatments.NeedsTreatment);
        Assert.DoesNotContain(ToothCondition.ExtraitAbsent, ConditionTreatments.NeedsTreatment);
        // Both still carry treatments, for when the dentist does plan one.
        Assert.NotEmpty(ConditionTreatments.For(ToothCondition.DentIncluse));
        Assert.NotEmpty(ConditionTreatments.For(ToothCondition.ExtraitAbsent));
    }

    [Fact]
    public void Every_Pathology_That_Needs_Treatment_Can_Offer_Something()
    {
        foreach (var condition in ConditionTreatments.NeedsTreatment)
        {
            // « À traiter » names no problem, so having nothing to suggest is the correct answer for it.
            if (condition == ToothCondition.ATraiter) continue;
            Assert.NotEmpty(Offered(condition));
        }
    }

    // The derived guard: adding a member to `ToothCondition` without deciding what treats it, and whether it is
    // work to do, would otherwise ship as a diagnosis that silently suggests nothing.
    [Fact]
    public void Every_Condition_Has_A_Deliberate_Entry()
    {
        foreach (var condition in Enum.GetValues<ToothCondition>())
        {
            var selectors = ConditionTreatments.For(condition);
            if (condition is ToothCondition.Sain or ToothCondition.ATraiter)
            {
                Assert.Empty(selectors);
                continue;
            }
            Assert.True(selectors.Count > 0, $"{condition} has no treatment mapping — add one to ConditionTreatments");
        }
    }

    [Fact]
    public void An_Empty_Selector_Matches_Nothing()
    {
        Assert.Empty(ConditionTreatments.RanksFor(null, null)
            .Where(r => r.Condition == ToothCondition.Carie));
    }

    // An act is recorded at the FIRST rung it satisfies. Without that, an extraction would answer « Carie » at
    // rank 3 and again at any later selector, and the client's « best first » ordering would be undefined.
    [Fact]
    public void An_Act_Holds_One_Rank_Per_Condition()
    {
        foreach (var (_, produces, category) in Catalogue)
        {
            var ranks = ConditionTreatments.RanksFor(produces, category);
            Assert.Equal(ranks.Select(r => r.Condition).Distinct().Count(), ranks.Count);
        }
    }
}
