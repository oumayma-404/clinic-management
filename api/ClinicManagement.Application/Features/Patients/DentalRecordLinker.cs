using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Shared odontogram/plan wiring used by the dental-record Create + Update handlers: closing charted
/// diagnoses when a tooth is treated, and marking a treatment-plan step "réalisé" from the record.
/// </summary>
public static class DentalRecordLinker
{
    /// <summary>Delete any open <see cref="ToothStateSource.Diagnosis"/> entries on teeth that this record now treats (AC-5).</summary>
    public static async Task ClearDiagnosesForTreatedTeethAsync(
        IToothStateRepository toothStateRepository,
        Guid patientId,
        IReadOnlyList<ToothState> newTreatmentStates,
        CancellationToken cancellationToken)
    {
        if (newTreatmentStates.Count == 0)
        {
            return;
        }

        var treatedTeeth = newTreatmentStates.Select(s => s.ToothNumber).ToHashSet();
        var existing = await toothStateRepository.GetByPatientIdAsync(patientId, cancellationToken);
        foreach (var diagnosis in existing.Where(s =>
                     s.Source == ToothStateSource.Diagnosis && treatedTeeth.Contains(s.ToothNumber)))
        {
            await toothStateRepository.DeleteAsync(diagnosis.Id, cancellationToken);
        }
    }

    /// <summary>
    /// Mark the given plan act — or one named <b>step</b> of it — "réalisé", linked to
    /// <paramref name="dentalRecordId"/> (AC-4). Returns a failure for a missing/cross-tenant plan, an unknown
    /// act or an unknown step; the plan's own guard (must be accepted/in progress) surfaces as an
    /// <see cref="InvalidOperationException"/> for the caller to translate.
    /// <para>
    /// ⚠️ <b>This is where a multi-séance act stops being unrepresentable.</b>
    /// <c>TreatmentPlanItem.MarkDone</c> refuses a second, <i>different</i> fiche, so before steps existed one
    /// devis line could only ever be evidenced by one fiche de soins — a bridge charted across three séances hit
    /// « Cet acte est déjà réalisé et rattaché à une autre fiche de soins » on the second. Each step holds its
    /// own record link, so the line now spans as many fiches as it has steps.
    /// </para>
    /// <para>
    /// When no step is identified the act-level path still runs, and for a stepped act that advances its next
    /// pending step rather than declaring the whole thing finished — see <c>TreatmentPlanItem.MarkDone</c>. So a
    /// séance booked before its act gained steps, or one that named no step, still records honestly instead of
    /// being refused.
    /// </para>
    /// <para>
    /// ⚠️ <b>One fiche closes EVERY step the séance carried, and that is the whole point of grouping.</b>
    /// « Préparation + empreinte dans la même séance » is the client's headline request and two rows on the
    /// wire, so a fiche that advanced only the one step its request happened to name left the empreinte
    /// « à planifier » against an appointment already in the past — and the act row then offered « Enregistrer
    /// la fiche » for a séance whose fiche exists, which opens a *second* fiche for one visit, whose own link
    /// resolves back to the already-done préparation and is refused. A dead end, reachable by doing exactly
    /// what the feature was built for.
    /// </para>
    /// <para>
    /// ⚠️ The set is read from the <b>appointment's own procedure rows</b> rather than taken from the request,
    /// deliberately: which steps a séance covers is a fact this database already holds
    /// (<c>AppointmentProcedure.TreatmentPlanItemStepId</c>), and a list assembled by a client is a list the
    /// next client forgets to assemble. The explicitly-named step is unioned in, so a caller with no
    /// appointment — a fiche recorded with no visit behind it — behaves exactly as before.
    /// </para>
    /// </summary>
    public static async Task<Result> LinkPlanItemAsync(
        ITreatmentPlanRepository treatmentPlanRepository,
        IAppointmentRepository appointmentRepository,
        Guid? treatmentPlanId,
        Guid treatmentPlanItemId,
        Guid patientId,
        Guid clinicId,
        Guid dentalRecordId,
        DateTime doneOn,
        CancellationToken cancellationToken,
        Guid? treatmentPlanItemStepId = null,
        Guid? appointmentId = null)
    {
        if (!treatmentPlanId.HasValue)
        {
            return Result.Failure("Le plan de traitement est requis pour lier l'acte.");
        }

        var plan = await treatmentPlanRepository.GetByIdAsync(treatmentPlanId.Value, cancellationToken);
        if (plan == null || plan.ClinicId != clinicId || plan.PatientId != patientId)
        {
            return Result.Failure("Plan de traitement introuvable.");
        }

        if (plan.Items.All(i => i.Id != treatmentPlanItemId))
        {
            return Result.Failure("Acte du plan introuvable.");
        }

        var stepIds = await ResolveStepsOfTheSeanceAsync(
            appointmentRepository, appointmentId, treatmentPlanItemId, clinicId,
            treatmentPlanItemStepId, cancellationToken);

        /*
         * ⚠️ **The named act, and only the named act.** A séance legitimately carries several devis acts, and it
         * is tempting to close all of them from the appointment — but a fiche names ONE
         * `TreatmentPlanItemId`, and the record modal proposes every booked act precisely so the dentist can
         * *delete* the ones that did not happen. So the booking says what was intended and the fiche says what
         * was done, and « réalisé » is a claim only the second is entitled to make. Marking the others would
         * write clinical evidence — and, through `RecomputeStatusFromSteps`, plan completion and the
         * échéancier — from an intention. Two steps of THIS act in one séance is the case the appointment does
         * legitimately answer, and that is what `ResolveStepsOfTheSeanceAsync` reads.
         */
        var item = plan.Items.First(i => i.Id == treatmentPlanItemId);

        {
            if (stepIds.Count > 0)
            {
                // Checked against THIS act's steps, not the plan's — a step id from another line of the same
                // devis would otherwise record progress against the wrong bridge. Same rule, same reason, as
                // AppointmentPlanLink.ValidateManyAsync.
                if (stepIds.Any(stepId => item.Steps.All(s => s.Id != stepId)))
                {
                    return Result.Failure("Étape du devis introuvable.");
                }

                /*
                 * In the protocol's own order, so a séance covering steps 1 and 2 records them in that order
                 * and the act's stored DoneDate/link end up on the LAST of them — which is what
                 * RecomputeStatusFromSteps reads when the act finishes.
                 *
                 * ⚠️ A step already evidenced by a DIFFERENT fiche is skipped, not re-marked. The appointment is
                 * the authority on which steps a séance covers, and a séance legitimately outlives one fiche:
                 * a step detached and re-recorded, or a visit whose steps were charted across two sittings,
                 * leaves rows here whose evidence is another record. `MarkDone` refuses that re-link — rightly,
                 * it would claim the step happened at a visit it did not — but the refusal took the WHOLE save
                 * down and named a step the dentist had not touched, so correcting one mis-attached step made
                 * the act permanently unrecordable. Skipping leaves the existing evidence exactly as it is and
                 * closes the steps this fiche really does attest.
                 */
                var closable = item.Steps
                    .Where(s => stepIds.Contains(s.Id))
                    .Where(s => !s.IsDone || s.LinkedDentalRecordId == null
                                || s.LinkedDentalRecordId == dentalRecordId)
                    .Select(s => s.Id)
                    .ToList();

                foreach (var stepId in closable)
                {
                    plan.MarkItemStepDone(treatmentPlanItemId, stepId, doneOn, dentalRecordId);
                }
            }
            else
            {
                plan.MarkItemDone(treatmentPlanItemId, doneOn, dentalRecordId);
            }
        }

        await treatmentPlanRepository.UpdateAsync(plan, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Every devis <b>act</b> the séance behind this fiche was booked for, each with the steps of it that the
    /// séance covers — unioned with the act and step the caller named.
    ///
    /// <para>An act mapping to an empty step set was booked whole, which sends the caller down the act-level
    /// path; and with no appointment (or one belonging to another clinic) the result is the named act alone,
    /// exactly as before.</para>
    ///
    /// <para>
    /// ⚠️ <b>Several acts, not one — the workspace invites the grouping and the fiche could not complete it.</b>
    /// Ticking two « À planifier » acts offers « 2 actes sélectionnés — Planifier ensemble — 1 RDV », and the
    /// resulting row reads « séance de 2 actes ». But this resolved a single act, so recording the visit left
    /// the second « À enregistrer » with its appointment already in the past, and the worklist kept listing it.
    /// The recovery — a second fiche for one visit — is the very dead end this function's step-level half was
    /// written to remove; the same shape across two <i>acts</i> was simply never applied.
    /// </para>
    /// </summary>
    /// <summary>
    /// Every step of <paramref name="namedItemId"/> this séance was booked to carry out — the one the caller
    /// named, plus any other step of the <b>same act</b> attached to the same appointment.
    /// <para>
    /// That second half is the multi-step séance, which is the case the feature exists for: « préparation +
    /// empreinte dans la même séance » arrives as two appointment rows sharing one act, and the fiche can only
    /// name one of them. Steps of a <i>different</i> act on the same appointment are deliberately not returned —
    /// see the note at the call site.
    /// </para>
    /// </summary>
    private static async Task<HashSet<Guid>> ResolveStepsOfTheSeanceAsync(
        IAppointmentRepository appointmentRepository,
        Guid? appointmentId,
        Guid namedItemId,
        Guid clinicId,
        Guid? namedStepId,
        CancellationToken cancellationToken)
    {
        var stepIds = new HashSet<Guid>();
        if (namedStepId.HasValue)
        {
            stepIds.Add(namedStepId.Value);
        }

        if (!appointmentId.HasValue)
        {
            return stepIds;
        }

        var appointment = await appointmentRepository.GetByIdAsync(appointmentId.Value, cancellationToken);
        // A missing or cross-tenant appointment contributes nothing rather than failing the save: the fiche is
        // the clinical record and it must be recordable even when the visit it names has since been deleted.
        if (appointment == null || appointment.ClinicId != clinicId)
        {
            return stepIds;
        }

        foreach (var procedure in appointment.Procedures)
        {
            if (procedure.TreatmentPlanItemId != namedItemId)
            {
                continue;
            }
            if (procedure.TreatmentPlanItemStepId.HasValue)
            {
                stepIds.Add(procedure.TreatmentPlanItemStepId.Value);
            }
        }

        return stepIds;
    }
}
