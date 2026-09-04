using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.UnitTests.Features.ProcedureTypes;

/// <summary>
/// The seeded price list against the profession's own floor.
///
/// <para>The Conseil National de l'Ordre des Médecins Dentistes de Tunisie publishes a
/// <i>barème d'honoraires minimums</i> (adopted 27 December 2020), and <b>article 30 of the code de déontologie
/// forbids a dentist charging below it</b>. A default that sits under the floor is therefore not a matter of
/// taste: the product would be inviting every clinic it seeds to break a rule its own regulator sets, and the
/// clinic would never know, because a seeded price looks like an answer rather than a suggestion.</para>
///
/// <para>Two rows did exactly that — « Détartrage » at 60 against a floor of 90, and « Blanchiment dentaire » at
/// 400 against 500. Nothing in the build could see it, because each row is perfectly plausible on its own.</para>
///
/// <para>⚠️ The map below is the <b>floor</b>, never the recommended price. Most rows sit deliberately above it
/// (the barème is a 2020 minimum, not a 2026 market rate), so this asserts <c>&gt;=</c> and never equality —
/// an assertion of equality here would fight every legitimate price rise.</para>
/// </summary>
public class ProcedureTypeCatalogSeedFloorTests
{
    /// <summary>
    /// Seed row name → CNOMDT minimum in TND. Only rows the barème actually covers appear; a seeded act with no
    /// published minimum (radiographie panoramique, séance orthodontique, soin d'enfant…) is unconstrained and
    /// is deliberately absent rather than given an invented floor.
    /// </summary>
    private static readonly Dictionary<string, decimal> Barème = new()
    {
        ["Consultation / examen bucco-dentaire"] = 40m,   // Consultation
        ["Radiographie rétro-alvéolaire"] = 20m,          // Dent par technique intra buccale, film rétro-alvéolaire
        ["Soin de carie / obturation"] = 90m,             // Traitement global : résine composite
        ["Traitement de canal (dévitalisation)"] = 70m,   // Pulpectomie — groupe incisivo-canin (the cheapest grade)
        ["Détartrage"] = 90m,                             // Détartrage complet sus gingival
        ["Traitement parodontal (surfaçage / curetage)"] = 70m, // Traitement des gingivites
        ["Extraction simple"] = 40m,                      // Extraction dentaire simple
        ["Extraction chirurgicale (sagesse / dent incluse)"] = 130m, // Extraction chirurgicale — dent enclavée
        ["Couronne / bridge (par élément)"] = 130m,       // Couronne métallique (the cheapest crown)
        ["Prothèse amovible (partielle / complète)"] = 140m, // Prothèses adjointes — de 1 à 3 dents
        ["Implant dentaire"] = 800m,                      // Chirurgie implantaire
        ["Traitement orthodontique (multi-attaches)"] = 3000m, // Traitement orthodontique multi-attache
        ["Blanchiment dentaire"] = 500m,                  // Eclaircissement dentaire (avec ou sans gouttière)
        ["Facette (par élément)"] = 600m,                 // Facette céramique — priced per veneer, like the crown row

        // The distinct acts added after reading the barème.
        ["Coiffage pulpaire"] = 30m,                      // Coiffage pulpaire / pulpectomie coronaire simple
        ["Inlay-core (reconstitution corono-radiculaire)"] = 80m, // Inlay core métallique
        ["Couronne provisoire"] = 60m,                    // Prothèse provisoire
        ["Extraction de racine (alvéolectomie)"] = 60m,   // Extraction de la ou des racines par alvéolectomie
        ["Gingivectomie"] = 50m,                          // Gingivectomie partielle
        ["Greffe osseuse / comblement"] = 700m,           // Expansion osseuse
        ["Scellement de sillons"] = 80m,                  // Résine de scellement des puits et fissures
        ["Application de fluor (par arcade)"] = 200m,     // Gouttière pour application de fluor, par arcade
        ["Couronne pédodontique préformée"] = 110m,       // Couronne pédodontique préformée
        ["Mainteneur d'espace fixe"] = 160m,              // Mainteneur d'espace fixe
        ["Gouttière occlusale (bruxisme)"] = 400m,        // Gouttière occlusale
    };

    [Fact]
    public void No_Seeded_Price_Sits_Below_The_CNOMDT_Minimum()
    {
        foreach (var row in ProcedureTypeCatalogSeed.Rows)
        {
            if (!Barème.TryGetValue(row.Name, out var floor)) continue;
            Assert.True(
                row.DefaultCost >= floor,
                $"« {row.Name} » is seeded at {row.DefaultCost:0.###} DT, below the CNOMDT minimum of {floor:0.###} DT "
                + "(barème d'honoraires minimums, 27/12/2020). Article 30 of the code de déontologie forbids it.");
        }
    }

    /// <summary>
    /// A guard keyed on names fails <b>open</b>: rename a row and its floor silently stops being checked. This is
    /// the non-vacuity half — most of the priced catalogue must actually be reached.
    /// </summary>
    [Fact]
    public void The_Floor_Map_Still_Matches_The_Catalogue()
    {
        var names = ProcedureTypeCatalogSeed.Rows.Select(r => r.Name).ToHashSet();

        foreach (var covered in Barème.Keys)
        {
            Assert.True(names.Contains(covered), $"« {covered} » is no longer a seeded row — the floor is unchecked");
        }

        // Free follow-up and the acts with no published minimum are the only rows allowed to go uncovered.
        var priced = ProcedureTypeCatalogSeed.Rows.Count(r => r.DefaultCost > 0);
        Assert.True(
            Barème.Count >= priced / 2,
            $"only {Barème.Count} of {priced} priced rows carry a floor — the map has fallen behind the catalogue");
    }

    /// <summary>
    /// An act must not write an odontogram state it does not produce.
    ///
    /// <para>A seeded act takes its resulting condition from its <b>discipline</b>, which is a coarse default and
    /// wrong for three rows: an inlay-core is filed under Prothèse fixe but leaves no crown, draining an abscess
    /// is filed under Chirurgie but removes no tooth, and a bone graft is filed under Implantologie but places no
    /// implant. Each would chart a state the patient does not have — invisibly, since the fiche saves happily and
    /// only the odontogram is wrong. The row-level override exists for exactly these, and this pins it.</para>
    /// </summary>
    [Fact]
    public void An_Act_Never_Charts_A_State_It_Does_Not_Produce()
    {
        var charted = ProcedureTypeCatalogSeed
            .CreateFor(Guid.NewGuid())
            .ToDictionary(p => p.Name, p => p.ResultingCondition);

        Assert.Null(charted["Inlay-core (reconstitution corono-radiculaire)"]);
        Assert.Null(charted["Incision d'abcès et drainage"]);
        Assert.Null(charted["Greffe osseuse / comblement"]);

        // Two more, found by running the ladders over the real catalogue rather than by reading the rows: a
        // coiffage is « à l'exclusion de l'obturation définitive » in the barème's own words, and a provisoire is
        // by definition replaced. Left on their disciplines' defaults each tied the first rung of a ladder, which
        // is what removes a diagnosis' pre-filled plan line — see SeededCatalogueSuggestionTests.
        Assert.Null(charted["Coiffage pulpaire"]);
        Assert.Null(charted["Couronne provisoire"]);

        // And the override did not go too far — the discipline's default still reaches the acts it is right for.
        Assert.Equal(ToothCondition.Obturation, charted["Soin de carie / obturation"]);
        Assert.Equal(ToothCondition.ExtraitAbsent, charted["Extraction de racine (alvéolectomie)"]);
        Assert.Equal(ToothCondition.TraitementDeCanal, charted["Retraitement endodontique"]);
        Assert.Equal(ToothCondition.Couronne, charted["Couronne / bridge (par élément)"]);
    }
}
