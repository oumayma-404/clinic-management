using System.Globalization;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// « Rendre au patient » (G3): a devis lowered below what it collected. The screen sends no method first, gets
/// <see cref="Code"/> back with the amount in the sentence, asks once, and re-sends with the method.
/// </summary>
public static class PlanRefund
{
    public const string Code = "plan-total-below-collected";

    /// <summary>Parse the optional method. A cheque is refused: nothing hands a cheque back to a patient here.</summary>
    public static bool TryParse(string? value, out PaymentMethod? method, out string? error)
    {
        method = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Enum.TryParse<PaymentMethod>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            error = "Mode de remboursement invalide.";
            return false;
        }
        if (parsed == PaymentMethod.Cheque)
        {
            error = "Un rendu au patient ne se fait pas par chèque.";
            return false;
        }
        method = parsed;
        return true;
    }

    /// <summary>True when the aggregate refused only because nobody confirmed the rendu yet.</summary>
    public static bool IsNeeded(TreatmentPlan plan, PaymentMethod? method) =>
        method is null && plan.ExcessCollected > 0m;

    // fr-FR pinned: « 300.000 DT » reads as three hundred thousand to a French reader.
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private static string Dt(decimal amount) => amount.ToString("#,##0.000", French) + " DT";

    public static string StopSentence(decimal amount) =>
        $"{Dt(amount)} encaissés sur ce devis dépassent le travail réalisé : ils sont à rendre au patient.";

    public static string Sentence(TreatmentPlan plan) =>
        $"{Dt(plan.AmountPaid)} ont déjà été encaissés pour un nouveau total de {Dt(plan.TotalPlanned)} : "
        + $"{Dt(plan.ExcessCollected)} sont à rendre au patient.";
}
