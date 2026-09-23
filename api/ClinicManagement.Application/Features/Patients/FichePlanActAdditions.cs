using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.TreatmentPlans;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>An act of this fiche the dentist is putting ON the devis the séance is carrying out.</summary>
/// <param name="ActIndex">
/// Its position in the request's <c>Acts</c>. The only identity an act has inside one save —
/// <c>DentalRecord.SetActs</c> regenerates every act id on every save, so there is no stable id to name, and
/// it is the unit <see cref="FicheExtraPlanActs"/> already binds in.
/// </param>
public sealed record FichePlanActAddition(int ActIndex);

/// <summary>
/// « Cet acte aussi va sur le devis » — a séance's <b>new</b> act joins the treatment it is being carried out
/// for, in the same transaction that records the séance.
/// </summary>
/// <remarks>
/// <para>
/// <b>The complaint.</b> A « prothèse provisoire » quoted 300 with 100 collected, séance 2 of 2, and a second
/// act done at the chair the same day. Its fee had exactly one destination: a note d'honoraires for the
/// séance — a separate document the devis' own balance never mentions. Typing it into « Encaissé sur le
/// traitement » instead was refused, correctly, because the treatment was still only worth what it was worth
/// before the act existed (<c>CollectOnTreatmentCommand</c>: <c>delta &gt; plan.Outstanding</c>). Nothing in
/// the product raised what the treatment was worth from the screen where the work is recorded.
/// </para>
/// <para>
/// ⚠️ <b>Here rather than in the browser, and that is the whole point of this class.</b> The frontend could
/// (and briefly did) amend the devis with its own call and then save the fiche. Three things are wrong with
/// that and all three are fixed by moving it: an amendment that lands while the fiche save then fails leaves
/// the devis holding a line for work nobody recorded; the refusals below are enforced in a browser, which is
/// not an authority; and the fee has to be sent twice — once as the act's cost, once as the devis line's —
/// which is two numbers that must agree.
/// </para>
/// <para>
/// ⚠️ <b>It runs AFTER <see cref="FicheExtraPlanActs.ResolveAsync"/>, and that order is the idempotency.</b>
/// An addition is applied only to an act index nothing has claimed — and on a re-save the act IS claimed,
/// because the devis line created last time is linked to this fiche and `ResolveAsync` reads it back. Without
/// that ordering every reopen of the fiche to fix a typo would quote the act again, with no gesture behind it
/// and no error anywhere: the <c>SetActs</c> trap, on a field that moves money.
/// </para>
/// <para>
/// ⚠️ <b>One séance per added act — declared THROUGH <c>TreatmentPlanStepProtocol</c>, not by skipping it.</b>
/// The act was carried out today, so applying the catalogue protocol would put a finished couronne on the devis
/// still owing two séances nobody will ever record, and the recall worklist would chase them. But « this act is
/// one séance » is an answer that helper already understands — an <b>empty confirmed list</b> — so it is stated
/// there rather than by bypassing the one owner of « what steps does an added act get? ».
/// <c>StepProtocolCoverageTests</c> is what caught the bypass, and it was right to.
/// </para>
/// <para>
/// ⚠️ <b>No plan version is checked, deliberately.</b> The fiche save already mutates this aggregate without
/// one (<see cref="DentalRecordLinker"/> marks steps done on it), and a fiche is entered clinical data: it
/// must not be refused because a colleague renamed a devis line while the dentist was typing. The write is
/// additive, so it cannot silently overwrite anybody's edit — which is the thing a version check is for.
/// </para>
/// </remarks>
public static class FichePlanActAdditions
{
    /// <summary>The refusal's own code, so a client branches on it and never on the French sentence.</summary>
    public const string RefusalCode = "dental_record_plan_addition_refused";

    /// <summary>
    /// The acts to store (each added one priced 0), the devis lines they became — shaped as
    /// <see cref="FicheExtraPlanActs.Extra"/> so the caller links them through the one linker — or the reason
    /// the save must not happen at all.
    /// </summary>
    public sealed record Applied(
        List<DentalRecordActInput> Acts,
        List<FicheExtraPlanActs.Extra> Added,
        string? Refusal = null)
    {
        public static Applied None(List<DentalRecordActInput> acts) =>
            new(acts, new List<FicheExtraPlanActs.Extra>());
    }

    public static async Task<Applied> ApplyAsync(
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        IProcedureTypeRepository procedureTypeRepository,
        List<DentalRecordActInput> acts,
        Guid? treatmentPlanId,
        Guid? primaryItemId,
        IReadOnlyList<FichePlanActAddition>? requested,
        IReadOnlyList<FicheExtraPlanActs.Extra> alreadyLinked,
        Guid clinicId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (treatmentPlanId is not { } planId
            || primaryItemId is not { } primaryId
            || requested is null or { Count: 0 }
            || acts.Count == 0)
        {
            return Applied.None(acts);
        }

        var plan = await planRepository.GetByIdAsync(planId, cancellationToken);
        // A missing or cross-tenant plan is not this helper's refusal to make — `DentalRecordLinker` reports it,
        // in French, on the same request. Same division of labour as `PlanCarriedActPricing`.
        if (plan == null || plan.ClinicId != clinicId)
        {
            return Applied.None(acts);
        }

        /*
         * Which act indices are spoken for already: the devis act this fiche is primarily recording, plus every
         * extra `FicheExtraPlanActs` bound — which on a re-save includes the lines a previous save of THIS fiche
         * created. That is what makes a second « Enregistrer » add nothing.
         */
        var claimed = new HashSet<int>(alreadyLinked.Select(e => e.ActIndex));
        var primary = plan.Items.FirstOrDefault(i => i.Id == primaryId);
        if (primary != null)
        {
            var primaryIndex = PlanCarriedAct.IndexIn(acts, primary);
            if (primaryIndex >= 0) claimed.Add(primaryIndex);
        }

        var pending = new List<int>();
        foreach (var addition in requested)
        {
            if (!claimed.Contains(addition.ActIndex) && !pending.Contains(addition.ActIndex))
            {
                pending.Add(addition.ActIndex);
            }
        }
        if (pending.Count == 0)
        {
            return Applied.None(acts);
        }

        /*
         * ⚠️ EVERY refusal before the first mutation. Below this point the aggregate is changed, and a devis
         * half-amended by a save that then fails is the illegible result this repository keeps paying for.
         */
        foreach (var index in pending)
        {
            if (index < 0 || index >= acts.Count)
            {
                return new Applied(acts, new List<FicheExtraPlanActs.Extra>(),
                    "Un acte à ajouter au devis n'a pas été reconnu. Rechargez la fiche et réessayez.");
            }

            /*
             * ⚠️ A hand-typed act is refused rather than guessed at. The devis line would carry no catalogue
             * identity, so nothing could bind it back to this act on the NEXT save — and an unbound line is
             * re-added every time the fiche is reopened. `PlanCarriedAct` takes the same stance for the same
             * reason: a wrong guess here moves money and writes clinical evidence.
             */
            if (acts[index].ProcedureTypeId is null)
            {
                return new Applied(acts, new List<FicheExtraPlanActs.Extra>(),
                    $"« {acts[index].ProcedureName} » est saisi à la main : choisissez-le dans le catalogue "
                    + "pour pouvoir l'ajouter au devis.");
            }
        }

        /*
         * ⚠️ A note d'honoraires that REPRESENTS the devis takes it out of « Solde patient », « Créances », la
         * caisse and the dashboard whole — so an act added to it would be a live debt in the one place nothing
         * looks. `AmendTreatmentPlanCommand` refuses this state in as many words, and the browser withholds the
         * control; this is the authority behind both.
         */
        var note = await PlanBridgeLookup.RepresentingNoteAsync(
            invoiceRepository, clinicId, planId, cancellationToken);
        if (note is not null)
        {
            return new Applied(acts, new List<FicheExtraPlanActs.Extra>(),
                $"Ce devis est facturé sur la note n° {note} : un acte ajouté maintenant n'apparaîtrait dans "
                + "aucun solde. Facturez cet acte sur la séance, ou corrigez la note.");
        }

        /*
         * ⚠️ **The plan must still be LIVE, and leaving this to the aggregate was a real defect rather than a
         * theoretical one.** `AddItems`' own `EnsureAmendable` refuses only a `Cancelled` plan — deliberately,
         * since « le médecin doit pouvoir tout corriger » — but `MarkItemDone` goes through `EnsureActive`,
         * which refuses everything outside `TreatmentPlanLifecycle.LiveStatuses`. So on a `Completed`,
         * `Stopped` or `WrittenOff` devis the line was added, the échéancier re-spread, and the save then threw
         * at the far end when the linker tried to mark the new act réalisé: a late, unexplained failure, after
         * the aggregate had already been changed in memory. Refused here, before anything moves, and by name.
         *
         * ⚠️ `Draft` IS live, and it stays admitted — the un-numbered refusal below is what covers a followed
         * treatment, and a numbered Draft is a treatment being collected on, which may legitimately grow.
         */
        if (!TreatmentPlanLifecycle.IsLive(plan.Status))
        {
            return new Applied(acts, new List<FicheExtraPlanActs.Extra>(),
                "Ce traitement n'est plus en cours : rouvrez-le avant d'y ajouter un acte, "
                + "ou facturez cet acte sur la séance.");
        }

        /*
         * ⚠️ An un-numbered treatment has no échéancier, so the respread below would BUILD it one — giving a
         * « Solde à régler » to a treatment nobody has been quoted for. That is `RespreadSchedule`'s own open
         * defect (board row G10) rather than this path's, and routing new money into it is how a known bug
         * becomes somebody's balance. Lift this once G10 lands.
         */
        if (plan.Number is null)
        {
            return new Applied(acts, new List<FicheExtraPlanActs.Extra>(),
                "Ce traitement n'a pas encore de devis : présentez le devis avant d'y ajouter un acte.");
        }

        var before = plan.Items.Select(i => i.Id).ToHashSet();
        plan.AddItems(pending.Select(i => new TreatmentPlanItemInput(
            null,
            acts[i].ProcedureName,
            // The act's own fee IS the devis line's — one number, so the two documents cannot disagree. The act
            // is zeroed below, once it has been copied.
            acts[i].Cost,
            acts[i].ProcedureTypeId,
            acts[i].ToothNumbers)));

        // The total moved, so the échéancier follows it: every collected row stays at exactly what it took and
        // the balance lands on one row. `Outstanding` becomes « ce qui restait + cet acte », which is the figure
        // the dentist was refused when they tried to collect it.
        plan.RespreadScheduleToTotal(ClinicClock.ClinicToday());
        // One amendment, one revision — adding three acts during one séance is one edit from the patient's side.
        plan.RecordAmendment();

        var added = plan.Items.Where(i => !before.Contains(i.Id)).ToList();

        /*
         * « Une séance » for each added act, said to the one helper that owns the question. An empty confirmed
         * list is its own vocabulary for exactly this, so the catalogue protocol is consulted and declined
         * rather than never asked — which is what `StepProtocolCoverageTests` exists to require.
         *
         * ⚠️ Indexed by `SequenceNumber`, as `ConfirmedFor` reads it, and scoped with `onlyItemIds` so an act
         * already on the devis in one séance on purpose is not re-cut into the catalogue's (F7).
         */
        var confirmed = new List<IReadOnlyList<TreatmentPlanItemStepInput>?>();
        var highest = added.Count == 0 ? -1 : added.Max(i => i.SequenceNumber);
        for (var seq = 0; seq <= highest; seq++)
        {
            confirmed.Add(added.Any(i => i.SequenceNumber == seq)
                ? Array.Empty<TreatmentPlanItemStepInput>()
                : null);
        }

        await TreatmentPlanStepProtocol.ApplyAsync(
            plan, clinicId, procedureTypeRepository, cancellationToken,
            confirmed, added.Select(i => i.Id).ToList());

        var result = new List<DentalRecordActInput>(acts);
        var extras = new List<FicheExtraPlanActs.Extra>();
        for (var n = 0; n < pending.Count && n < added.Count; n++)
        {
            var index = pending[n];
            // ⚠️ Positional, and it is sound: `AddItems` appends in the order it is given and the aggregate
            // keeps that order. Matching on the designation instead would collapse two identical acts — a
            // second couronne on another tooth — onto one devis line.
            result[index] = result[index] with
            {
                Cost = 0m,
                // `UnitCost` goes to 0 beside `Cost`, never left behind: a per-tooth act reopens from its unit
                // price, so a stale one restores the fee on the next « Enregistrer ».
                UnitCost = result[index].UnitCost is null ? null : 0m,
            };
            extras.Add(new FicheExtraPlanActs.Extra(added[n], index, null));
        }

        await planRepository.UpdateAsync(plan, cancellationToken);
        logger.LogInformation(
            "Fiche added {Count} act(s) to treatment {PlanId} ({Number}); total is now {Total}",
            extras.Count, planId, plan.Number, plan.TotalPlanned);

        return new Applied(result, extras);
    }
}
