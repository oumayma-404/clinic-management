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
}
