using Microsoft.Extensions.Logging;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// « Un acte porté par un devis est à 0 » — imposed on the fiche de soins by the server, not merely offered by
/// the browser.
///
/// <para>
/// <b>The hole it closes.</b> The rule already existed and was already enforced where a séance is <i>booked</i>:
/// <c>AppointmentProcedureSelection.PriceForPlanLinkedAct</c> overwrites whatever price a client sends for an act
/// a devis carries, on the stated grounds that the server imposes it « plutôt que de faire confiance au client ».
/// The fiche — the screen that actually creates the money — asked nobody. Its act arrived at 0, the field was
/// freely editable, and overtyping it with the act's real fee raised a note d'honoraires for work the treatment
/// already prices. That note carries no <c>TreatmentPlanId</c>, so <c>PlanBillingRules.BilledPlanIds</c> cannot
/// de-duplicate it, and the patient ends up owing the same act twice on two unrelated documents.
/// </para>
///
/// <para>
/// ⚠️ <b>It forces rather than refuses</b>, matching its appointment-side twin. A refusal would be unanswerable
/// from a UI that (rightly) locks the field, and the 0 is not a discount anybody granted — it is « already priced
/// elsewhere », which is a fact about the treatment and not a decision about this séance.
/// </para>
///
/// <para>
/// ⚠️ <b>At most ONE act is zeroed, and that bound is deliberate.</b> A devis line is one act; a séance covering
/// two <i>steps</i> of it is still one act on the fiche (two rows on the appointment, one card on screen). Zeroing
/// every act that happens to share the catalogue procedure would silently un-bill a second, independent crown done
/// the same day on another tooth — a money loss with no error, which is the shape of defect this whole change is
/// removing rather than one to add. The browser's <c>markBilledOnPlan</c> matches the same way for the same reason.
/// </para>
/// </summary>
public static class PlanCarriedActPricing
{
    /// <summary>
    /// Returns <paramref name="acts"/> with the act this séance carries for <paramref name="treatmentPlanItemId"/>
    /// priced at 0, or the list untouched when the fiche carries no treatment act.
    /// </summary>
    /// <param name="treatmentPlanId">The devis named by the request; null leaves everything alone.</param>
    /// <param name="treatmentPlanItemId">The devis act named by the request; null leaves everything alone.</param>
    public static async Task<List<DentalRecordActInput>> ImposeAsync(
        ITreatmentPlanRepository planRepository,
        List<DentalRecordActInput> acts,
        Guid? treatmentPlanId,
        Guid? treatmentPlanItemId,
        Guid clinicId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (treatmentPlanId is not { } planId || treatmentPlanItemId is not { } itemId || acts.Count == 0)
        {
            return acts;
        }

        var plan = await planRepository.GetByIdAsync(planId, cancellationToken);
        // A missing or cross-tenant plan is not this helper's refusal to make — `DentalRecordLinker` reports it,
        // in French, on the same request. Imposing nothing here leaves that the single authority on the failure.
        if (plan == null || plan.ClinicId != clinicId)
        {
            return acts;
        }

        var item = plan.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null)
        {
            return acts;
        }

        /*
         * Which of the fiche's acts is the devis'? The catalogue procedure, when the devis line names one — the
         * only identity the two rows share, since `SetActs` regenerates every act id on every save.
         *
         * With no `ProcedureTypeId` on the devis line (a hand-typed one), a SINGLE-act fiche is unambiguous and
         * anything larger is left alone rather than zeroed on a guess: silently un-billing the wrong act is worse
         * than leaving a hand-typed line for the dentist to correct.
         */
        var index = item.ProcedureTypeId is { } procedureTypeId
            ? acts.FindIndex(a => a.ProcedureTypeId == procedureTypeId)
            : acts.Count == 1 ? 0 : -1;

        if (index < 0 || (acts[index].Cost == 0m && acts[index].UnitCost is null or 0m))
        {
            return acts;
        }

        var carried = acts[index];
        logger.LogInformation(
            "Act « {Act} » is carried by treatment {PlanId}; imposing 0 in place of the {Cost} sent",
            carried.ProcedureName, planId, carried.Cost);

        var imposed = new List<DentalRecordActInput>(acts)
        {
            // `UnitCost` goes to 0 beside `Cost`, never left behind: the fiche reopens a per-tooth act from its
            // unit price, so a stale one would restore the fee the next time anybody pressed « Enregistrer ».
            [index] = carried with { Cost = 0m, UnitCost = carried.UnitCost is null ? null : 0m },
        };
        return imposed;
    }
}
