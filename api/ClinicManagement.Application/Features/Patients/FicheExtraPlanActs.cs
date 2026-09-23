using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>One more devis act a fiche carries out, beside its primary <c>TreatmentPlanItemId</c>.</summary>
public sealed record FichePlanItemLink(Guid TreatmentPlanItemId, Guid? TreatmentPlanItemStepId = null);

/// <summary>
/// A séance that carried out <b>several</b> acts of one devis — the fiche closes all of them (C4).
/// </summary>
/// <remarks>
/// <para>
/// A fiche had one <c>TreatmentPlanItemId</c>, so a visit booked with two devis acts closed act 1 and left act 2
/// « à enregistrer »; its « Enregistrer la fiche » then reopened the same fiche on act 1, and act 2 could never be
/// marked réalisé. The extras ride beside the primary link: each takes the next act of the fiche with its
/// procedure (never one already claimed), is priced 0 like the primary (<see cref="PlanCarriedActPricing"/>), and
/// is linked by the same <see cref="DentalRecordLinker"/>.
/// </para>
/// <para>
/// ⚠️ On a re-save the extras are also read back from the devis itself — an act already linked to this fiche stays
/// one, so a client that sends only the primary cannot quietly re-price the second act.
/// </para>
/// </remarks>
public static class FicheExtraPlanActs
{
    public sealed record Extra(TreatmentPlanItem Item, int ActIndex, Guid? StepId);

    public sealed record Resolved(List<DentalRecordActInput> Acts, List<Extra> Extras)
    {
        public static Resolved None(List<DentalRecordActInput> acts) => new(acts, new List<Extra>());
    }

    public static async Task<Resolved> ResolveAsync(
        ITreatmentPlanRepository planRepository,
        List<DentalRecordActInput> acts,
        Guid? treatmentPlanId,
        Guid? primaryItemId,
        IReadOnlyList<FichePlanItemLink>? requested,
        Guid? existingRecordId,
        Guid clinicId,
        CancellationToken cancellationToken)
    {
        if (treatmentPlanId is not { } planId || primaryItemId is not { } primaryId || acts.Count == 0)
        {
            return Resolved.None(acts);
        }

        var plan = await planRepository.GetByIdAsync(planId, cancellationToken);
        if (plan == null || plan.ClinicId != clinicId)
        {
            return Resolved.None(acts);
        }

        var wanted = new List<(Guid ItemId, Guid? StepId)>();
        foreach (var link in requested ?? Array.Empty<FichePlanItemLink>())
        {
            if (link.TreatmentPlanItemId != primaryId && wanted.All(w => w.ItemId != link.TreatmentPlanItemId))
            {
                wanted.Add((link.TreatmentPlanItemId, link.TreatmentPlanItemStepId));
            }
        }
        if (existingRecordId is { } recordId)
        {
            foreach (var held in plan.Items.Where(i => i.Id != primaryId
                         && (i.LinkedDentalRecordId == recordId
                             || i.Steps.Any(st => st.LinkedDentalRecordId == recordId))))
            {
                if (wanted.All(w => w.ItemId != held.Id))
                {
                    wanted.Add((held.Id, held.Steps.FirstOrDefault(st => st.LinkedDentalRecordId == recordId)?.Id));
                }
            }
        }
        if (wanted.Count == 0)
        {
            return Resolved.None(acts);
        }

        var taken = new HashSet<int>();
        var primary = plan.Items.FirstOrDefault(i => i.Id == primaryId);
        if (primary != null)
        {
            var primaryIndex = PlanCarriedAct.IndexIn(acts, primary);
            if (primaryIndex >= 0) taken.Add(primaryIndex);
        }

        var result = new List<DentalRecordActInput>(acts);
        var extras = new List<Extra>();
        foreach (var (itemId, stepId) in wanted)
        {
            var item = plan.Items.FirstOrDefault(i => i.Id == itemId);
            if (item?.ProcedureTypeId is not { } procedureTypeId || item.Status == TreatmentPlanItemStatus.Withdrawn)
            {
                continue;
            }

            var index = -1;
            for (var i = 0; i < result.Count; i++)
            {
                if (!taken.Contains(i) && result[i].ProcedureTypeId == procedureTypeId)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                continue;
            }

            taken.Add(index);
            result[index] = result[index] with
            {
                Cost = 0m,
                UnitCost = result[index].UnitCost is null ? null : 0m,
            };
            extras.Add(new Extra(item, index, stepId));
        }

        return new Resolved(result, extras);
    }

    /// <summary>
    /// Withhold the end state of every extra act that is not finished yet — <see cref="ToothChartingRules"/>'s rule,
    /// applied at each extra's own act.
    /// </summary>
    public static IReadOnlyList<DentalRecordActInput> Chartable(
        IReadOnlyList<DentalRecordActInput> acts,
        IReadOnlyList<(int ActIndex, bool ItemIsComplete)> extras)
    {
        if (extras.Count == 0)
        {
            return acts;
        }

        var result = new List<DentalRecordActInput>(acts);
        foreach (var (index, complete) in extras)
        {
            if (!complete && index >= 0 && index < result.Count
                && result[index].ResultingCondition is not (null or ToothCondition.Sain))
            {
                result[index] = result[index] with { ResultingCondition = null };
            }
        }
        return result;
    }
}
