using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.ProcedureTypes;

/// <summary>
/// A curated starter set of the dental procedures a Tunisian private practice actually performs — NOT limited
/// to CNAM-reimbursed acts (includes couronnes, bridges, implants, blanchiment, facettes, orthodontie…). Each
/// row prefills a clinic's <see cref="ProcedureType"/> menu with a typical Tunisian private-practice price
/// (TND) and duration; every value is fully editable by the clinic afterwards. Prices are indicative midpoints
/// (they vary widely by city/tier), meant as a starting point. Used both to seed a new clinic on creation and
/// to backfill an existing clinic's menu on demand.
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

    public static IReadOnlyList<SeedRow> Rows { get; } = new List<SeedRow>
    {
        new("Consultation / examen bucco-dentaire", 30, 40m, "Consultation"),
        new("Consultation d'urgence", 20, 50m, "Consultation"),
        new("Radiographie rétro-alvéolaire", 10, 20m, "Radiologie"),
        new("Radiographie panoramique", 15, 40m, "Radiologie"),
        new("Composite / obturation (1 face)", 30, 70m, "Soins conservateurs"),
        new("Composite / obturation (2-3 faces)", 45, 110m, "Soins conservateurs"),
        new("Coiffage pulpaire / soin dentinaire", 30, 60m, "Soins conservateurs"),
        new("Dévitalisation monoradiculaire (1 canal)", 45, 120m, "Endodontie"),
        new("Dévitalisation pluriradiculaire (molaire)", 60, 180m, "Endodontie"),
        new("Retraitement endodontique", 90, 250m, "Endodontie"),
        new("Inlay-core / reconstitution corono-radiculaire", 45, 200m, "Endodontie"),
        new("Détartrage (2 arcades)", 30, 60m, "Parodontologie"),
        new("Surfaçage radiculaire / curetage (par quadrant)", 45, 120m, "Parodontologie"),
        new("Gingivectomie", 45, 200m, "Parodontologie"),
        new("Extraction simple", 30, 60m, "Chirurgie/Extraction"),
        new("Extraction dent de sagesse (érupté)", 45, 120m, "Chirurgie/Extraction"),
        new("Extraction chirurgicale / dent incluse", 60, 250m, "Chirurgie/Extraction"),
        new("Couronne céramo-métallique", 60, 450m, "Prothèse fixe"),
        new("Couronne céramique / zircone (E-max)", 60, 750m, "Prothèse fixe"),
        new("Bridge céramo-métallique (par élément)", 60, 450m, "Prothèse fixe"),
        new("Inlay / Onlay céramique", 60, 500m, "Prothèse fixe"),
        new("Prothèse complète résine (par mâchoire)", 60, 800m, "Prothèse amovible"),
        new("Prothèse partielle résine", 45, 500m, "Prothèse amovible"),
        new("Prothèse stellite (châssis métallique)", 60, 1000m, "Prothèse amovible"),
        new("Réparation / rebasage de prothèse", 30, 120m, "Prothèse amovible"),
        new("Pose d'implant (vis + pilier)", 60, 1500m, "Implantologie"),
        new("Implant + couronne (complet)", 90, 2200m, "Implantologie"),
        new("Greffe osseuse / comblement / sinus lift", 90, 1200m, "Implantologie"),
        new("Consultation + bilan orthodontique", 45, 100m, "Orthodontie"),
        new("Traitement multi-attaches métallique (bagues)", 60, 3500m, "Orthodontie"),
        new("Traitement multi-attaches céramique", 60, 4500m, "Orthodontie"),
        new("Gouttières transparentes (aligneurs)", 45, 8000m, "Orthodontie"),
        new("Appareil amovible (enfant / interception)", 30, 800m, "Orthodontie"),
        new("Contention post-orthodontique", 30, 300m, "Orthodontie"),
        new("Blanchiment au fauteuil (lampe/laser)", 60, 400m, "Esthétique"),
        new("Blanchiment ambulatoire (gouttières à domicile)", 30, 300m, "Esthétique"),
        new("Facette céramique (E-max)", 60, 700m, "Esthétique"),
        new("Facette composite (directe)", 45, 250m, "Esthétique"),
        new("Consultation enfant", 20, 40m, "Pédodontie"),
        new("Scellement de sillons (par dent)", 20, 50m, "Pédodontie"),
        new("Extraction dent de lait", 15, 40m, "Pédodontie"),
        new("Pulpotomie (dent de lait)", 30, 80m, "Pédodontie"),
    };

    /// <summary>Build fresh <see cref="ProcedureType"/> entities for a clinic from the starter rows.</summary>
    public static IEnumerable<ProcedureType> CreateFor(Guid clinicId) =>
        Rows.Select(r => new ProcedureType(
            Guid.NewGuid(),
            clinicId,
            r.Name,
            r.DurationMinutes,
            ColorHex.FromString(CategoryColors.TryGetValue(r.Category, out var color) ? color : FallbackColor),
            r.Category,
            r.DefaultCost));
}
