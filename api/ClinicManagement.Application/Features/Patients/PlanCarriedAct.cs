using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// « Which act of this fiche is the one the devis carries? » — asked in one place, because two answers to it
/// produce two different fiches from the same save.
///
/// <para>
/// A séance legitimately holds several acts: the devis act being carried out plus, say, a filling done the same
/// day. Everything the treatment owns about that one act — its fee is imposed at 0
/// (<see cref="PlanCarriedActPricing"/>), its end state is withheld from the odontogram until the last séance
/// (<see cref="ToothChartingRules"/>) — has to key on the <b>same</b> act, or one rule fires on the filling and
/// the other on the implant.
/// </para>
///
/// <para>
/// ⚠️ It was a private expression inside <see cref="PlanCarriedActPricing"/>, and extracting it is not tidying:
/// the odontogram rule is its second reader, and a re-typed copy of « the act whose procedure matches the plan
/// line, or the only act when the line names no procedure » is precisely the shape this repository keeps
/// finding — a correct rule wired to one call site.
/// </para>
/// </summary>
public static class PlanCarriedAct
{
    /// <summary>
    /// The index of the fiche's act that this devis line carries, or <c>-1</c> when none can be identified.
    ///
    /// <para>
    /// ⚠️ <b>At most one, and never a match by name.</b> The devis line is one act; zeroing (or withholding) every
    /// act sharing a catalogue procedure would silently un-bill a second crown done the same day. A line naming no
    /// procedure — a hand-typed devis row — is resolvable only when the fiche holds a single act; anything larger
    /// is left alone rather than guessed at, since a wrong guess moves money and writes clinical evidence.
    /// </para>
    /// </summary>
    public static int IndexIn(IReadOnlyList<DentalRecordActInput> acts, TreatmentPlanItem item)
    {
        if (acts.Count == 0)
        {
            return -1;
        }

        if (item.ProcedureTypeId is { } procedureTypeId)
        {
            for (var i = 0; i < acts.Count; i++)
            {
                if (acts[i].ProcedureTypeId == procedureTypeId)
                {
                    return i;
                }
            }

            return -1;
        }

        return acts.Count == 1 ? 0 : -1;
    }

    /// <summary>
    /// « Ce devis dit porter un acte que la fiche ne contient pas » — the state a fiche must never be saved in.
    ///
    /// <para>
    /// ⚠️ <b>Changing the act on a devis-carried fiche used to reach exactly this state, in silence.</b>
    /// « Changer d'acte » rewrites the card and leaves <c>TreatmentPlanItemId</c> pointing at the line it was
    /// opened on, so the save arrived claiming to carry out a couronne while recording an extraction. Nothing
    /// refused it: <see cref="IndexIn"/> simply answered <c>-1</c>, so <see cref="PlanCarriedActPricing"/>
    /// imposed no 0 and <c>ToothChartingRules</c> withheld nothing, while <c>DentalRecordLinker</c> went on to
    /// mark the <b>couronne's</b> step done against a fiche that does not record it. The devis then read
    /// « fait » for work nobody had carried out, and the extraction — still wearing the browser's locked 0 —
    /// was billed nothing at all.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>It is narrower than « IndexIn returned -1 », and every clause of the narrowing is load-bearing.</b>
    /// <c>-1</c> has two innocent causes that must stay innocent: a devis line naming no catalogue act (a
    /// hand-typed row) on a fiche holding more than one act, and a fiche whose act is itself hand-typed. The
    /// second is not hypothetical — every fiche opened on a devis step before <c>planItemPrefill</c> carried a
    /// <c>ProcedureTypeId</c> recorded a free-text act, and those rows are on the live database. Refusing them
    /// would make an old plan fiche impossible to re-save to fix a typo. So the refusal fires only when both
    /// sides name catalogue acts and they disagree, which is precisely the « I changed the act » case.
    /// </para>
    /// </summary>
    public static bool NamesAnActTheFicheDoesNotHold(
        IReadOnlyList<DentalRecordActInput> acts, TreatmentPlanItem item) =>
        item.ProcedureTypeId is not null
        && acts.Count > 0
        && acts.All(a => a.ProcedureTypeId is not null)
        && IndexIn(acts, item) < 0;
}
