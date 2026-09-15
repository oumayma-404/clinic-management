using ClinicManagement.Domain.Services;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// « Une note d'honoraires représente-t-elle ce devis ? » — the one answer, shared by every échéancier money
/// write.
///
/// <para>
/// ⚠️ <b>It exists because the record side asked and the void side did not.</b>
/// <see cref="Commands.RecordInstallmentPaymentCommandHandler"/> refuses a payment on a bridged plan by name;
/// <see cref="Commands.VoidInstallmentPaymentCommandHandler"/> went straight from the tenant check to the
/// aggregate. Once <c>CarryOverPlanPaymentsAsync</c> has copied the receipts onto the note, every installment
/// money read excludes the plan — so voiding the plan-side row changed <b>nothing</b>: the invoice
/// <c>Payment</c> stayed live, la caisse went on counting it, and the user was told « paiement annulé ». The
/// repository's most-documented defect shape, a correct rule wired to one call site out of two.
/// </para>
/// <para>
/// The authority is <see cref="PlanBillingRules.RepresentsItsPlan"/> over the light link projection, the same
/// rule the money reads de-duplicate through — so a <c>Draft</c> bridge (which represents nothing) and a
/// <c>Cancelled</c> one (which is void) both leave the échéancier live and collectable.
/// </para>
/// </summary>
public static class PlanBridgeLookup
{
    /// <summary>
    /// The number of the note d'honoraires that represents <paramref name="planId"/>, or <c>null</c> when none
    /// does. Null is the ordinary answer for every plan that was never billed.
    /// </summary>
    public static async Task<string?> RepresentingNoteAsync(
        IInvoiceRepository invoiceRepository,
        Guid clinicId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        var links = await invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken);

        return links
            .Where(l => l.TreatmentPlanId == planId && PlanBillingRules.RepresentsItsPlan(l.Status))
            .Select(l => l.Number)
            .FirstOrDefault();
    }
}
