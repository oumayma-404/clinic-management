using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// Release every note d'honoraires attached to a devis that is being voided — the retroactive-continuation
/// link, detached in the <b>same save</b> as the void so the two facts can never disagree.
///
/// <para>
/// ⚠️ <b>Why it is a shared helper and not a block inside one handler.</b> It began life inside
/// <c>CancelTreatmentPlanCommand</c>, and it is load-bearing: the link is write-once, so without the detach a
/// note went on naming a cancelled devis for ever, the continuation could never be re-run for that fiche (its
/// acts still matched the « already tracked » query), and the fiche itself became <b>undeletable</b> — deleting
/// one un-marks a step on a cancelled plan, which the aggregate refuses, surfacing as
/// « Erreur lors de la suppression. Veuillez réessayer. »
/// </para>
/// <para>
/// When « Annuler le devis » folded into « Arrêter le traitement » (the branch is derived from
/// <c>TreatmentPlan.StopWouldCancel</c>, not chosen by the user), a second caller needed the identical
/// behaviour. Copying it would have been this repository's most-documented defect shape — a correct rule wired
/// to one call site — so it moved here and both handlers call it.
/// </para>
/// <para>
/// The note's own money is untouched: it keeps its lines, its number and its payments, and simply stops
/// speaking for a devis that no longer exists.
/// </para>
/// </summary>
public static class TreatmentPlanBridgeRelease
{
    /// <summary>
    /// Detach every invoice in this clinic that names <paramref name="planId"/>. Stages the updates on the
    /// repository; the caller's <c>IUnitOfWork.SaveChangesAsync</c> commits them alongside the plan.
    /// </summary>
    /// <returns>How many notes were released — for the caller's log line.</returns>
    public static async Task<int> DetachAsync(
        IInvoiceRepository invoiceRepository,
        Guid clinicId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        var links = await invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken);
        var released = 0;

        foreach (var link in links.Where(l => l.TreatmentPlanId == planId))
        {
            var invoice = await invoiceRepository.GetByIdAsync(link.InvoiceId, cancellationToken);
            // Cross-tenant rows cannot reach here through the clinic-scoped query above; the guard is the
            // repository-level one this solution applies everywhere a read is followed by a write.
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                continue;
            }

            invoice.DetachFromTreatmentPlan(planId);
            await invoiceRepository.UpdateAsync(invoice, cancellationToken);
            released++;
        }

        return released;
    }
}
