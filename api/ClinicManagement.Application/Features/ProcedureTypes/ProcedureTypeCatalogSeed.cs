using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.ProcedureTypes;

/// <summary>
/// A deliberately **general** starter set of the dental procedures a Tunisian private practice performs — one
/// row per act a dentist actually books and bills, not per clinical variant (practitioner feedback: the earlier
/// 43-row list split hairs — « 1 face » vs « 2-3 faces », mono- vs pluriradiculaire, céramo-métal vs zircone —
/// which read as noise). Broad coverage, NOT limited to CNAM-reimbursed acts (couronnes, implants, blanchiment,
/// facettes, orthodontie…). Each row prefills a clinic's <see cref="ProcedureType"/> menu with a typical
/// Tunisian private-practice price (TND) and duration; every value is fully editable by the clinic afterwards,
/// and clinics wanting a finer breakdown add their own rows. Prices are indicative midpoints (they vary widely
/// by city/tier), meant as a starting point. Used both to seed a new clinic on creation and to backfill an
/// existing clinic's menu on demand.
///
/// <para><b>⚠️ No row may sit below the CNOMDT floor.</b> The Conseil National de l'Ordre des Médecins Dentistes
/// de Tunisie publishes a <i>barème d'honoraires minimums</i> (adopted 27 December 2020), and article 30 of the
/// code de déontologie forbids a dentist charging under it. Two rows shipped below it — détartrage at 60 against
/// a floor of 90, and blanchiment at 400 against 500 — so the product's own defaults invited every new clinic to
/// break that rule. <c>ProcedureTypeCatalogSeedFloorTests</c> holds the whole list against the barème now.</para>
///
/// <para>⚠️ The list is 19 rows <b>on purpose</b>. It was 43 and was cut on practitioner feedback for splitting
/// hairs — « 1 face » vs « 2-3 faces », mono- vs pluriradiculaire, céramo-métal vs zircone (<c>feef4d8a</c>).
/// Re-adding a clinical <i>variant</i> of a row that already exists is re-opening a closed decision; a genuinely
/// distinct act that has no row at all is a different question.</para>
/// </summary>
public static class ProcedureTypeCatalogSeed
{
    /// <summary>
    /// One starter act. <paramref name="ResultingCondition"/> is <b>tri-state</b>: <c>null</c> takes the
    /// discipline's default from <see cref="CategoryResultingConditions"/>, and <c>Sain</c> means « this act
    /// charts nothing » — the entity already reads <c>Sain</c> as no state, so the two say different things.
    ///
    /// <para>⚠️ It exists because three acts would otherwise be mis-charted by their own discipline: an
    /// inlay-core is filed under Prothèse fixe but does not put a crown on the tooth, draining an abscess is
    /// filed under Chirurgie but does not remove it, and a bone graft is filed under Implantologie but is not an
    /// implant. Each would have written a state the patient's odontogram does not have.</para>
    /// </summary>
    public sealed record SeedRow(
        string Name,
        int DurationMinutes,
        decimal DefaultCost,
        string Category,
        ToothCondition? ResultingCondition = null);

    /// <summary>
    /// Category → palette colour (must be a value <see cref="ColorHex"/> accepts; the picker's palette is served
    /// from there, so there is no second copy to keep in step).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Every category must own a distinct hex, and two pairs did not.</b> « Esthétique » shipped on
    /// « Orthodontie »'s <c>#FB7185</c> and « Pédodontie » on « Parodontologie »'s <c>#6BAA75</c>, so four
    /// disciplines rendered as two colours: a facette and a séance orthodontique were the same pink in the agenda,
    /// and a détartrage and a soin d'enfant the same green. The colour is the only thing distinguishing two
    /// appointment blocks at a glance, so the collision cost exactly the capability it exists to provide — and it
    /// was invisible in code review, because each line is correct on its own.
    /// <para>
    /// The replacements are the <i>Clair</i> nuance of the same hue family the collision was in, so the discipline
    /// still reads as related to its neighbour rather than as an unrelated new colour:
    /// « Esthétique » → rose Clair, « Pédodontie » → vert Clair. <c>CategoryColoursAreDistinctTests</c> is the
    /// derived guard; a thirteenth category that reuses a hex fails there rather than in a cabinet's agenda.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> CategoryColors = new()
    {
        ["Consultation"] = "#6C757D",
        ["Radiologie"] = "#60A5FA",
        ["Soins conservateurs"] = "#2A9D8F",
        ["Endodontie"] = "#4F83CC",
        ["Parodontologie"] = "#6BAA75",
        ["Chirurgie/Extraction"] = "#E76F51",
        ["Prothèse fixe"] = "#9B8EDC",
        ["Prothèse amovible"] = "#5EEAD4",
        ["Implantologie"] = "#E9A23B",
        ["Orthodontie"] = "#FB7185",
        ["Esthétique"] = "#F79AA6",
        ["Pédodontie"] = "#93C79C",
    };

    private const string FallbackColor = "#6C757D";

    // Default odontogram state a procedure of each category produces (editable per procedure; overridable per act).
    // Coarse defaults — categories mixing states (e.g. Pédodontie, Prothèse fixe with bridges) rely on the
    // admin/per-act override. Categories not listed produce no tooth-state change.
    private static readonly Dictionary<string, ToothCondition?> CategoryResultingConditions = new()
    {
        ["Soins conservateurs"] = ToothCondition.Obturation,
        ["Endodontie"] = ToothCondition.TraitementDeCanal,
        ["Chirurgie/Extraction"] = ToothCondition.ExtraitAbsent,
        ["Prothèse fixe"] = ToothCondition.Couronne,
        ["Implantologie"] = ToothCondition.Implant,
    };

    public static IReadOnlyList<SeedRow> Rows { get; } = new List<SeedRow>
    {
        new("Consultation / examen bucco-dentaire", 30, 40m, "Consultation"),
        new("Contrôle / suivi", 15, 0m, "Consultation"),
        new("Radiographie rétro-alvéolaire", 10, 20m, "Radiologie"),
        new("Radiographie panoramique", 15, 40m, "Radiologie"),
        new("Soin de carie / obturation", 40, 90m, "Soins conservateurs"),
        new("Traitement de canal (dévitalisation)", 60, 150m, "Endodontie"),
        new("Détartrage", 30, 90m, "Parodontologie"),
        new("Traitement parodontal (surfaçage / curetage)", 45, 120m, "Parodontologie"),
        new("Extraction simple", 30, 60m, "Chirurgie/Extraction"),
        new("Extraction chirurgicale (sagesse / dent incluse)", 60, 200m, "Chirurgie/Extraction"),
        new("Couronne / bridge (par élément)", 60, 500m, "Prothèse fixe"),
        new("Prothèse amovible (partielle / complète)", 60, 800m, "Prothèse amovible"),
        new("Réparation / rebasage de prothèse", 30, 120m, "Prothèse amovible"),
        new("Implant dentaire", 60, 1500m, "Implantologie"),
        new("Traitement orthodontique (multi-attaches)", 60, 3500m, "Orthodontie"),
        new("Séance orthodontique (contrôle / activation)", 30, 80m, "Orthodontie"),
        new("Blanchiment dentaire", 60, 500m, "Esthétique"),
        new("Facette", 60, 700m, "Esthétique"),
        new("Soin dentaire enfant (dent de lait)", 30, 60m, "Pédodontie"),

        /*
         * ── Actes distincts, ajoutés après relecture du barème de l'Ordre ───────────────────────────────────
         *
         * ⚠️ Each of these is an act with NO row above, never a grade of one that has. The list was cut 43 → 19
         * on practitioner feedback for splitting hairs (« 1 face » vs « 2-3 faces », mono- vs pluriradiculaire,
         * céramo-métal vs zircone), and that decision stands: nothing here re-opens it. What the cut also took
         * out, as collateral, were procedures a dentist books and bills in their own right — a coiffage is not
         * an obturation, an inlay-core is not a couronne, a scellement de sillons is not a soin.
         *
         * Prices are the CNOMDT barème d'honoraires minimums (27/12/2020) where it covers the act, and marked
         * « estimation » where it does not. They are floors, not recommendations — see the class docstring.
         */
        new("Coiffage pulpaire", 30, 30m, "Soins conservateurs"),                       // barème 30
        new("Retraitement endodontique", 90, 250m, "Endodontie"),                       // estimation
        new("Inlay-core (reconstitution corono-radiculaire)", 45, 80m, "Prothèse fixe",
            // Charts nothing: the core is placed, the crown that covers it is a separate act.
            ToothCondition.Sain),                                                       // barème 80
        new("Couronne provisoire", 30, 60m, "Prothèse fixe"),                           // barème 60
        new("Extraction de racine (alvéolectomie)", 40, 60m, "Chirurgie/Extraction"),   // barème 60
        new("Incision d'abcès et drainage", 20, 40m, "Chirurgie/Extraction",
            // Charts nothing: the tooth stays. Its discipline's default would have recorded it as extracted.
            ToothCondition.Sain),                                                       // estimation
        new("Gingivectomie", 45, 50m, "Parodontologie"),                                // barème 50 (partielle)
        new("Frénectomie", 45, 100m, "Parodontologie"),                                 // estimation
        new("Greffe osseuse / comblement", 60, 700m, "Implantologie",
            // Charts nothing: preparing the bone is not placing an implant.
            ToothCondition.Sain),                                                       // barème 700
        new("Scellement de sillons", 30, 80m, "Pédodontie"),                            // barème 80
        new("Application de fluor (par arcade)", 20, 200m, "Pédodontie"),               // barème 200
        new("Couronne pédodontique préformée", 40, 110m, "Pédodontie"),                 // barème 110
        new("Mainteneur d'espace fixe", 40, 160m, "Pédodontie"),                        // barème 160
        new("Gouttière occlusale (bruxisme)", 45, 400m, "Prothèse amovible"),           // barème 400
        new("Contention post-orthodontique", 30, 300m, "Orthodontie"),                  // estimation
    };

    /// <summary>
    /// Build fresh <see cref="ProcedureType"/> entities for a clinic from the starter rows.
    /// <para>
    /// ⚠️ Every argument is <b>named</b>, and that is not tidying. This call used to pass <c>r.Category</c>
    /// positionally into the constructor's <c>description</c> slot — there was no category column to put it in —
    /// so nineteen acts per clinic carried their discipline in a field the act form labels « Description
    /// (optionnel) », and the catalogue picker had to group on it while documenting that it was not allowed to
    /// trust it. Now that both parameters exist and are adjacent nullable strings, positional arguments are one
    /// transposition away from re-creating exactly that bug silently.
    /// </para>
    /// </summary>
    public static IEnumerable<ProcedureType> CreateFor(Guid clinicId) =>
        Rows.Select(r => new ProcedureType(
            id: Guid.NewGuid(),
            clinicId: clinicId,
            name: r.Name,
            defaultDurationMinutes: r.DurationMinutes,
            color: ColorHex.FromString(CategoryColors.TryGetValue(r.Category, out var color) ? color : FallbackColor),
            // A seeded act has no description — the starter row carries a name, a price and a discipline, and
            // inventing prose for it would be putting words in the clinic's mouth.
            description: null,
            defaultCost: r.DefaultCost,
            // The row's own answer wins; only a row that gives none falls back to its discipline's default.
            resultingCondition: r.ResultingCondition
                ?? (CategoryResultingConditions.TryGetValue(r.Category, out var condition) ? condition : null),
            category: r.Category));
}
