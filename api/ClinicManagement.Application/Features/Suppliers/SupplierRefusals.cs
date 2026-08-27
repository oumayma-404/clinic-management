namespace ClinicManagement.Application.Features.Suppliers;

/// <summary>
/// The refusals this feature can produce, French sentence and machine-readable code together.
/// <para>
/// ⚠️ <b>One place, because the sentence and the code are one statement.</b> Three copies is how a reworded
/// message silently stops matching the code it was paired with — the <c>Contains("déjà facturée")</c> defect this
/// repo deleted in <c>adoption-gaps-remediation</c>, arrived at from the other direction.
/// </para>
/// </summary>
public static class SupplierRefusals
{
    public const string DuplicateCode = "supplier_duplicate";
    public const string InUseCode = "supplier_in_use";

    /// <summary>AC-1 — names the record that already exists, so the user can go and look at it.</summary>
    public static string Duplicate(string existingName) =>
        $"Un fournisseur « {existingName} » existe déjà dans ce cabinet. " +
        "Modifiez-le plutôt que d'en créer un second.";

    /// <summary>
    /// AC-4 — names <b>what</b> is in the way, per kind, and points at the alternative.
    /// <para>
    /// The two counts are stated separately rather than summed: « 3 articles de stock » sends somebody to the
    /// stockroom and « 3 bons de prothèse » to the laboratory screen, while a bare « 3 » sends them looking in
    /// the wrong place.
    /// </para>
    /// </summary>
    public static string InUse(int stockItems, int labOrders)
    {
        var parts = new List<string>(2);
        if (stockItems > 0)
        {
            parts.Add($"{stockItems} article{(stockItems > 1 ? "s" : "")} de stock");
        }

        if (labOrders > 0)
        {
            parts.Add($"{labOrders} bon{(labOrders > 1 ? "s" : "")} de prothèse");
        }

        return $"{string.Join(" et ", parts)} référencent ce fournisseur. Désactivez-le plutôt : " +
               "il disparaîtra des listes de sélection sans effacer les liens existants.";
    }

    public const string NotFound = "Fournisseur introuvable.";
}
