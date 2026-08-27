namespace ClinicManagement.Domain.Services;

/// <summary>
/// What a <see cref="Entities.StockItem"/> is — « Consommables médicaux », « Protection (EPI) » — and the single
/// authority on how a typed one is spelled.
/// <para>
/// ⚠️ <b>These six labels used to be English storage keys mapped to French at display time</b>
/// (<c>"PPE"</c> → « Protection (EPI) »), which is this repo's standing convention for a <i>closed</i> value set.
/// The set stopped being closed: `GET /api/stock` already served the clinic's own distinct categories as a filter
/// facet, the picker already offered an unknown stored value back, and nothing ever refused a category typed
/// straight into the database. A half-closed set is the worst of both — the browser held a map that could not
/// cover what the server returned, so a clinic-authored category rendered raw beside six translated ones. So the
/// storage key is the French label now, the migration rewrites the six existing keys, and
/// <c>STOCK_CATEGORIES</c>/<c>STOCK_CATEGORY_LABELS_FR</c> are deleted from the browser.
/// </para>
/// <para>
/// ⚠️ <see cref="Normalize"/> folds the <b>legacy English key</b> too, and that is not tidiness: the migration
/// rewrites the rows, but an older client, a bookmarked <c>?category=PPE</c> filter and a CSV import can all
/// still present one. Folding costs nothing and is the difference between « Protection (EPI) » and a seventh
/// category nobody can see.
/// </para>
/// </summary>
public static class StockCategories
{
    /// <summary>
    /// The six the product shipped with, translated in place. Ordered as a stockroom is walked — what is
    /// consumed at the chair first, what is consumed at the desk last — not alphabetically.
    /// </summary>
    public static readonly IReadOnlyList<string> Canonical = new[]
    {
        "Consommables médicaux",
        "Protection (EPI)",
        "Médicaments",
        "Équipement médical",
        "Fournitures de laboratoire",
        "Fournitures de bureau",
    };

    /// <summary>
    /// The English keys the product persisted before this feature, and the French label each becomes.
    /// <para>
    /// ⚠️ <b>The migration's rewrite reads from here.</b> A second copy of the pairs written as SQL literals is
    /// how the migration and the runtime fold end up disagreeing about one of six rows — and the one they
    /// disagree about is invisible until a clinic filters on it.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyKeys = new Dictionary<string, string>
    {
        ["Medical Supplies"] = "Consommables médicaux",
        ["PPE"] = "Protection (EPI)",
        ["Medications"] = "Médicaments",
        ["Medical Equipment"] = "Équipement médical",
        ["Lab Supplies"] = "Fournitures de laboratoire",
        ["Office Supplies"] = "Fournitures de bureau",
    };

    /// <summary>
    /// The canonical spelling of <paramref name="value"/> — folding a legacy English key onto its French label
    /// first — the trimmed value if it is a category of the clinic's own, or <c>null</c> for blank.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var folded = CategoryFolding.Fold(trimmed);

        foreach (var legacy in LegacyKeys)
        {
            if (CategoryFolding.Fold(legacy.Key) == folded)
            {
                return legacy.Value;
            }
        }

        return CategoryFolding.NormalizeAgainst(trimmed, Canonical);
    }

    /// <summary>Whether <paramref name="value"/> names one of the suggested categories.</summary>
    public static bool IsCanonical(string? value) => CategoryFolding.IsCanonicalIn(value, Canonical);
}
