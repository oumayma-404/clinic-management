using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// When an act's finished state may be written onto the patient's odontogram.
///
/// <para>
/// ⚠️ <b>A restoration records work that is DONE, and the chart was asserting it from the first séance.</b>
/// <see cref="ToothCondition"/>'s own documentation draws the line — a <i>pathology</i> is a problem to plan
/// against, a <i>restoration</i> (<c>Obturation</c>, <c>Couronne</c>, <c>Implant</c>, …) « records work already
/// done » — and <c>DentalRecordActParser.BuildToothStates</c> wrote one on every fiche save, with no notion of
/// whether the act it belongs to was finished. A multi-séance act is chiffré once and carried out over weeks, so
/// its fiche for step 1 (préparation, extraction, empreinte) already carried the catalogue's
/// <c>ResultingCondition</c> and charted the end of a treatment that had barely started.
/// </para>
///
/// <para>
/// Measured on the live database — seven rows, every one written from a step 1 of 2:
/// three teeth charted <b>Implant</b> for a patient whose implant is placed in the last séance, two charted
/// <b>Extrait/absent</b> while the extraction was half done, two charted <b>Obturation</b>. No error anywhere;
/// the odontogram simply described a mouth the patient did not have, for the length of the treatment.
/// </para>
///
/// <para>
/// ⚠️ <b>Its twin is <c>DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync</c>, and the two must move
/// together.</b> That helper deletes the open « À traiter » diagnosis on every tooth the new states name — so on
/// séance 1 the chart both claimed the work was finished <i>and</i> forgot it had ever been needed. It is driven
/// off the states this rule produces, so withholding them withholds the deletion in the same breath, and the
/// tooth keeps saying « à traiter » until the treatment really ends. That is not a coincidence to rely on
/// silently: it is why this returns a filtered ACT list rather than filtering the states afterwards.
/// </para>
/// </summary>
public static class ToothChartingRules
{
    /// <summary>
    /// The acts to build odontogram entries from — the fiche's own list, with the devis act's
    /// <c>ResultingCondition</c> cleared while its treatment is still under way.
    ///
    /// <para>
    /// Clearing the condition rather than dropping the act is deliberate: <c>BuildToothStates</c> already skips
    /// <c>null</c>/<c>Sain</c>, so nothing downstream needs a new parameter, and the act keeps its teeth, its
    /// note and its surfaces for every other reader.
    /// </para>
    /// <para>
    /// ⚠️ <b>The returned list is for the odontogram alone and must never be the one stored on the record.</b>
    /// The fiche keeps the condition the dentist chose — it is what the act will chart when the treatment ends,
    /// and what re-opening the fiche has to show.
    /// </para>
    /// </summary>
    /// <param name="item">The devis act this fiche carries out, or null when the fiche is not on a plan.</param>
    /// <param name="itemIsComplete">
    /// Whether that act is <b>finished</b> after this save — read from the aggregate once the step has been
    /// marked, never guessed from the step's rank: a séance can close two steps at once, a protocol can be
    /// re-cut mid-treatment, and an act booked whole has no steps at all and finishes on its first fiche.
    /// </param>
    public static IReadOnlyList<DentalRecordActInput> ChartableActs(
        IReadOnlyList<DentalRecordActInput> acts,
        TreatmentPlanItem? item,
        bool itemIsComplete)
    {
        if (item is null || itemIsComplete || acts.Count == 0)
        {
            return acts;
        }

        // The same act the fee is imposed on — see `PlanCarriedAct` for why that has to be one answer.
        var index = PlanCarriedAct.IndexIn(acts, item);
        if (index < 0 || acts[index].ResultingCondition is null or ToothCondition.Sain)
        {
            return acts;
        }

        return new List<DentalRecordActInput>(acts)
        {
            [index] = acts[index] with { ResultingCondition = null },
        };
    }
}
