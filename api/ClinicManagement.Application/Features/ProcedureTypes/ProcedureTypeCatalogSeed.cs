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
/// </summary>
public static class ProcedureTypeCatalogSeed
{
    public sealed record SeedRow(string Name, int DurationMinutes, decimal DefaultCost, string Category);

    // Category → palette colour (must be a value ColorHex accepts / the frontend palette mirrors).
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
        ["Esthétique"] = "#FB7185",
        ["Pédodontie"] = "#6BAA75",
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
        new("Détartrage", 30, 60m, "Parodontologie"),
        new("Traitement parodontal (surfaçage / curetage)", 45, 120m, "Parodontologie"),
        new("Extraction simple", 30, 60m, "Chirurgie/Extraction"),
        new("Extraction chirurgicale (sagesse / dent incluse)", 60, 200m, "Chirurgie/Extraction"),
        new("Couronne / bridge (par élément)", 60, 500m, "Prothèse fixe"),
        new("Prothèse amovible (partielle / complète)", 60, 800m, "Prothèse amovible"),
        new("Réparation / rebasage de prothèse", 30, 120m, "Prothèse amovible"),
        new("Implant dentaire", 60, 1500m, "Implantologie"),
        new("Traitement orthodontique (multi-attaches)", 60, 3500m, "Orthodontie"),
        new("Séance orthodontique (contrôle / activation)", 30, 80m, "Orthodontie"),
        new("Blanchiment dentaire", 60, 400m, "Esthétique"),
        new("Facette", 60, 700m, "Esthétique"),
        new("Soin dentaire enfant (dent de lait)", 30, 60m, "Pédodontie"),
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
            resultingCondition: CategoryResultingConditions.TryGetValue(r.Category, out var condition) ? condition : null,
            category: r.Category));
}
