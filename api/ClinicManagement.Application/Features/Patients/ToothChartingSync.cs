using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Re-draw the odontogram for the fiches a devis act evidences, after something other than a fiche save
/// changed whether that act is finished.
///
/// <para>
/// ⚠️ <b><see cref="ToothChartingRules.ChartableActs"/> had exactly two callers, both fiche commands — so the
/// chart was written once, at the séance, and never revisited.</b> Four operations move an act across the
/// finished/unfinished line without touching a fiche:
/// <list type="bullet">
/// <item>« Détacher la fiche » (<c>UnmarkTreatmentPlanItemDoneCommand</c>) returns the act to « prévu », so the
/// end state must be withheld again — the chart went on asserting « Implant » for an implant the devis now says
/// was never placed;</item>
/// <item>the step-level twin (<c>UnmarkTreatmentPlanItemStepCommand</c>), identically;</item>
/// <item>« Arrêter le traitement » parks the unrealised acts, which un-finishes anything part-way through;</item>
/// <item>and the mirror — <c>MarkTreatmentPlanItemDoneCommand</c> <b>completes</b> a multi-séance act, so the
/// state the rule was withholding becomes chartable and was never written. The devis read « Réalisé » and the
/// tooth was blank.</item>
/// </list>
/// Neither direction errors anywhere. The rule is unchanged; only the caller set grows.
/// </para>
/// <para>
/// ⚠️ <b>The record ids must be captured BEFORE the aggregate is mutated.</b> Un-marking clears the very
/// pointers this needs (<c>TreatmentPlanItem.LinkedDentalRecordId</c> and each step's), so reading them
/// afterwards finds nothing and the chart silently keeps the stale state — the defect, one line later.
/// <see cref="EvidencingRecordIds"/> is that capture.
/// </para>
/// <para>
/// ⚠️ It stages its writes on the repositories and never saves: the caller commits them in the <b>same</b>
/// transaction as the plan, so the chart and the devis can never disagree about one act.
/// </para>
/// </summary>
public static class ToothChartingSync
{
    /// <summary>
    /// Every fiche de soins that evidences this act — its own link plus each step's. Call it <b>before</b> the
    /// mutation; see the type's own note.
    /// </summary>
    public static IReadOnlyList<Guid> EvidencingRecordIds(TreatmentPlanItem item) =>
        item.Steps
            .Select(s => s.LinkedDentalRecordId)
            .Append(item.LinkedDentalRecordId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Rebuild the odontogram entries of <paramref name="recordIds"/> from what those fiches actually record,
    /// with <paramref name="item"/>'s end state withheld while the act is unfinished.
    /// </summary>
    /// <param name="item">The devis act whose done-ness just changed. Its <b>current</b> status is read.</param>
    /// <param name="recordIds">From <see cref="EvidencingRecordIds"/>, captured before the mutation.</param>
    public static async Task ApplyAsync(
        TreatmentPlanItem item,
        IReadOnlyCollection<Guid> recordIds,
        Guid clinicId,
        IDentalRecordRepository dentalRecordRepository,
        IToothStateRepository toothStateRepository,
        CancellationToken cancellationToken)
    {
        if (recordIds.Count == 0)
        {
            return;
        }

        var itemIsComplete = item.Status == Domain.Enums.TreatmentPlanItemStatus.Done;

        foreach (var recordId in recordIds)
        {
            var record = await dentalRecordRepository.GetByIdAsync(recordId, cancellationToken);

            // Tenant isolation: a fiche from another clinic reads as absent. The ids came off this clinic's own
            // plan, so this can only fire on corrupt data — and the repository-level guard is the one this
            // solution applies everywhere a read is followed by a write.
            if (record == null || record.ClinicId != clinicId)
            {
                continue;
            }

            var acts = record.Acts
                .Select(a => new DentalRecordActInput(
                    a.ProcedureTypeId,
                    a.ProcedureName,
                    a.Cost,
                    a.UnitCost,
                    a.IsPerTooth,
                    a.ToothNumbers,
                    a.ResultingCondition,
                    a.Surfaces,
                    a.Note,
                    a.PonticToothNumbers,
                    a.ImplantPilierToothNumbers,
                    a.IsUnfinished))
                .ToList();

            // Delete and rebuild this fiche's whole set, exactly as `UpdateDentalRecordCommand` does — a
            // withheld state has to REMOVE the row an earlier save wrote, not merely decline to write one.
            foreach (var state in await toothStateRepository.GetByDentalRecordIdAsync(recordId, cancellationToken))
            {
                await toothStateRepository.DeleteAsync(state.Id, cancellationToken);
            }

            var chartable = ToothChartingRules.ChartableActs(acts, item, itemIsComplete);

            var toothStates = DentalRecordActParser
                .BuildToothStates(
                    chartable, record.PatientId, record.ClinicId, record.InterventionDate, record.Id)
                .ToList();

            // The rule's twin, driven off the states above exactly as both fiche commands drive it: withholding
            // the state withholds the deletion in the same breath, so the tooth keeps its « à traiter » while
            // the treatment is unfinished and loses it when the act really lands.
            await DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync(
                toothStateRepository, record.PatientId, toothStates, cancellationToken);

            foreach (var toothState in toothStates)
            {
                await toothStateRepository.AddAsync(toothState, cancellationToken);
            }
        }
    }
}
