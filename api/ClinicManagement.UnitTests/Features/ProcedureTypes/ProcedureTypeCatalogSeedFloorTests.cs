using ClinicManagement.Application.Features.ProcedureTypes;

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
        ["Facette"] = 600m,                               // Facette céramique
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
}
