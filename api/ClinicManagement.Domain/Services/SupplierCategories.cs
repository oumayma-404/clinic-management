namespace ClinicManagement.Domain.Services;

/// <summary>
/// What an external <see cref="Entities.Supplier"/> <i>is</i> to the cabinet — « Laboratoire de prothèse »,
/// « Consommables », « Analyses / Anatomopathologie » — and the single authority on how a typed one is spelled.
/// <para>
/// <b>A « fournisseur » here is any outside contact the practice orders from or sends work to</b>, not only a
/// stockroom supplier: the prothésiste who makes the crowns, the laboratory that reads a biopsy, the dépôt that
/// delivers the composite, and the technician who services the fauteuil are all the same kind of record — a name,
/// a category and a number somebody needs to reach. That is why the categories below span laboratories and goods
/// rather than goods alone, and why both <c>StockItem</c> and <c>LabWorkOrder</c> point at this one aggregate
/// instead of each carrying its own free-text name.
/// </para>
/// <para>
/// Deliberately the same shape as <see cref="ProcedureTypeCategories"/>: an <b>open</b> suggestion set, not a
/// constraint, and therefore <b>no category table and no category CRUD screen</b>. A practice that deals with a
/// « Menuisier » or an « Informaticien » files them verbatim, and a closed enum would mean a code change to
/// record a contact the cabinet already works with.
/// </para>
/// <para>
/// ⚠️ <b><see cref="Normalize"/> is what makes an open set survivable, and every write path must go through
/// it.</b> The value of a category is that contacts sharing one group together, and « prothèse », « Prothese »
/// and « PROTHÈSE » are three groups to PostgreSQL and one to a dentist (AC-2).
/// </para>
/// </summary>
public static class SupplierCategories
{
    /// <summary>
    /// Ordered by how often a cabinet reaches for one — the laboratory it chases weekly first, the occasional
    /// trades last — rather than alphabetically.
    /// </summary>
    public static readonly IReadOnlyList<string> Canonical = new[]
    {
        "Laboratoire de prothèse",
        "Analyses / Anatomopathologie",
        "Consommables",
        "Matériel et équipement",
        "Pharmacie et médicaments",
        "Implantologie",
        "Orthodontie",
        "Radiologie / Imagerie",
        "Hygiène et stérilisation",
        "Maintenance et services",
        "Fournitures de bureau",
    };

    /// <summary>
    /// The canonical spelling of <paramref name="value"/> if it names one of the <see cref="Canonical"/>
    /// categories, the trimmed value if it is a category of the clinic's own, or <c>null</c> for blank.
    /// </summary>
    public static string? Normalize(string? value) => CategoryFolding.NormalizeAgainst(value, Canonical);

    /// <summary>Whether <paramref name="value"/> names one of the suggested categories.</summary>
    public static bool IsCanonical(string? value) => CategoryFolding.IsCanonicalIn(value, Canonical);
}
