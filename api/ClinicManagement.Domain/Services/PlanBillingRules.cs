using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// The single authority on which treatment plans carry patient debt, shared by every money read
/// (« Solde patient », « Créances », la caisse and the dashboard) so they can never report different
/// figures for the same data. Pure, no persistence — the sibling of <see cref="InvoiceCalculator"/>.
///
/// Two rules:
/// <list type="number">
/// <item>Only a <b>committed</b> plan is debt. A <c>Draft</c> devis is an unaccepted quote and a
/// <c>Cancelled</c> one is void, so neither contributes — including its hand-built échéancier.</item>
/// <item>A plan bridged into a <b>real</b> (non-Draft, non-Cancelled) invoice is <i>represented</i> by that
/// invoice. It is counted once, through the invoice, and never a second time through the plan.</item>
/// </list>
///
/// Both rules apply to <b>outstanding</b> balances (what a patient still owes). They deliberately do
/// <b>not</b> apply to collected cash: an installment payment and an invoice payment are two distinct
/// receipts, and the devis→facture bridge copies no payment onto the invoice, so suppressing a bridged
/// plan's collections would erase real money from the caisse rather than de-duplicate it.
/// </summary>
public static class PlanBillingRules
{
    /// <summary>
    /// The plan statuses that carry debt. Used both in memory and as the SQL filter of the repository's
    /// installment aggregates, so the rule is stated once.
    /// </summary>
    public static readonly IReadOnlyCollection<TreatmentPlanStatus> DebtBearingPlanStatuses = new[]
    {
        TreatmentPlanStatus.Accepted,
        TreatmentPlanStatus.InProgress,
        TreatmentPlanStatus.Completed
    };

    /// <summary>True when a plan in this status contributes to what the patient owes.</summary>
    public static bool CarriesDebt(TreatmentPlanStatus status) => status switch
    {
        TreatmentPlanStatus.Accepted => true,
        TreatmentPlanStatus.InProgress => true,
        TreatmentPlanStatus.Completed => true,
        _ => false
    };

    /// <summary>
    /// True when an invoice in this status represents — and therefore replaces — the plan it was bridged
    /// from. A <c>Draft</c> invoice is not billed yet and a <c>Cancelled</c> one is void, so in both cases
    /// the plan keeps carrying its own balance (which is what makes an amendment block escapable).
    /// </summary>
    public static bool RepresentsItsPlan(InvoiceStatus status) =>
        status != InvoiceStatus.Draft && status != InvoiceStatus.Cancelled;

    /// <summary>
    /// The plan ids already represented by an invoice, from fully loaded invoices. Callers that have the
    /// aggregates in hand (« Solde patient ») use this overload.
    /// </summary>
    public static HashSet<Guid> BilledPlanIds(IEnumerable<Invoice> invoices) =>
        invoices
            .Where(i => i.TreatmentPlanId.HasValue && RepresentsItsPlan(i.Status))
            .Select(i => i.TreatmentPlanId!.Value)
            .ToHashSet();

    /// <summary>
    /// The plan ids already represented by an invoice, from the light bridge-link projection
    /// (<c>IInvoiceRepository.GetTreatmentPlanLinksAsync</c>). Clinic-wide reads use this overload so a
    /// de-duplication never has to load invoice lines and payments.
    /// </summary>
    public static HashSet<Guid> BilledPlanIds(
        IEnumerable<(Guid TreatmentPlanId, Guid InvoiceId, string? Number, InvoiceStatus Status)> links) =>
        links
            .Where(l => RepresentsItsPlan(l.Status))
            .Select(l => l.TreatmentPlanId)
            .ToHashSet();
}
