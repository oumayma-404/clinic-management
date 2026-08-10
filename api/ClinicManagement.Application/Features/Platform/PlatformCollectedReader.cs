using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// « Encaissé par le cabinet » — what a practice itself collected over a window, as the vendor console reports it
/// (<c>platform-console</c> AC-2.1).
///
/// <para><b>This makes the console the FIFTH money read in the product</b>, after la caisse, l'extrait, le
/// tableau de bord and « Total encaissé » on /factures. The other four are held to one figure by
/// <c>MoneyReadConsistencyTests</c>, and this one joins them — which is the entire reason it is a shared reader
/// rather than four repository calls inside <c>ClinicActivityCounterJob</c>. A private copy would be untestable
/// against its siblings, and the first time the bridged-plan de-dup changed, the vendor would be quoting a
/// practice a turnover that practice's own caisse contradicts. That is the worst possible place in this product
/// for two answers.</para>
///
/// <para>⚠️ <b>Net of avoirs</b>, matching « encaissé » everywhere else: a refunded payment is money the cabinet
/// no longer has. Expenses are <i>not</i> subtracted — this is what came in, not what was left over, and the
/// vendor has no business reporting a practice's profit.</para>
///
/// <para>⚠️ The plan side goes through <c>PlanBillingRules.BilledPlanIds</c>. Without that de-dup a devis bridged
/// into a note has its carried-over payments counted twice — once on the invoice track, once on the plan.</para>
/// </summary>
public static class PlatformCollectedReader
{
    /// <param name="toInclusive">The window's last <b>tick</b>, not the next midnight: every money read in this
    /// codebase is inclusive on both ends, and the exclusive bound counts a midnight payment in two periods.</param>
    public static async Task<decimal> ReadAsync(
        IInvoiceRepository invoices,
        ITreatmentPlanRepository plans,
        ICreditNoteRepository creditNotes,
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        CancellationToken cancellationToken = default)
    {
        var billedPlanIds = PlanBillingRules.BilledPlanIds(
            await invoices.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));

        var invoiceCollected = await invoices.GetCollectedBetweenAsync(
            clinicId, from, toInclusive, cancellationToken: cancellationToken);
        var installmentCollected = await plans.GetInstallmentCollectedBetweenAsync(
            clinicId, from, toInclusive, billedPlanIds, cancellationToken);
        var refunds = await creditNotes.GetRefundedBetweenAsync(clinicId, from, toInclusive, cancellationToken);

        return InvoiceCalculator.RoundMoney(invoiceCollected + installmentCollected - refunds);
    }
}
