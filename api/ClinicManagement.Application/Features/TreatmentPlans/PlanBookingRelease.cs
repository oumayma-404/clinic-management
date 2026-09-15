using ClinicManagement.Domain.Entities;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// Let the visits booked for a set of devis acts go, and report the ones that are left with nothing to do —
/// the caller cancels those once the plan itself is saved.
///
/// <para>
/// ⚠️ <b>Decided per procedure ROW, never per plan link.</b> A séance may legitimately carry a devis act
/// <i>and</i> a walk-in détartrage, and that visit still has a reason to happen once the devis act goes.
/// <c>SetProcedures</c> re-derives the lead-act snapshot and <c>TreatmentPlanItemId</c> from the rows it is
/// handed, so dropping a row is all this has to do — and <c>AgreedCost</c> is carried across on every kept
/// row, because the list is replace-semantics and a row re-sent without its price silently reverts to the
/// catalogue tarif.
/// </para>
/// <para>
/// ⚠️ A <b>Completed</b> visit is never cancelled and never rewritten, whatever it is left carrying. It
/// happened; only its link to a released act is untrue, and every derivation in this feature runs
/// act → appointment, never the reverse.
/// </para>
/// <para>
/// ⚠️ A <b>Cancelled</b> or <b>NoShow</b> visit is not cancelled either — it books nothing — but its links
/// <b>are</b> cleared, which is what this helper's second caller needed and the amend path had missed. The
/// reference is soft (no FK, deliberately, so a deletion cannot erase a marker), so a cancelled visit kept a
/// <c>TreatmentPlanItemId</c> pointing at a row that no longer exists, with nothing to catch it.
/// </para>
/// <para>
/// It was a private method of <c>AmendTreatmentPlanCommandHandler</c> and was <b>moved</b> rather than copied:
/// <c>ReassignTreatmentPlanPatientCommand</c> has to release exactly the same way, and « a correct rule wired
/// to one call site » is this repository's most-documented defect.
/// </para>
/// </summary>
public static class PlanBookingRelease
{
    /// <summary>
    /// Stage the release of every visit booked for <paramref name="releasedItemIds"/> on the repository.
    /// </summary>
    /// <returns>The ids of the visits that now carry nothing, to be cancelled after the plan is saved.</returns>
    public static async Task<List<Guid>> ReleaseAsync(
        IReadOnlyCollection<Guid> releasedItemIds,
        Guid clinicId,
        IAppointmentRepository appointmentRepository,
        CancellationToken cancellationToken)
    {
        if (releasedItemIds.Count == 0)
        {
            return new List<Guid>();
        }

        var released = releasedItemIds.ToHashSet();
        var appointments = await appointmentRepository.GetByTreatmentPlanItemIdsAsync(
            clinicId, releasedItemIds.ToList(), cancellationToken);

        var toCancel = new List<Guid>();
        foreach (var appointment in appointments)
        {
            if (appointment.Status == AppointmentStatus.Completed)
            {
                continue;
            }

            var pointsAtReleased = appointment.Procedures.Any(p =>
                p.TreatmentPlanItemId.HasValue && released.Contains(p.TreatmentPlanItemId.Value));
            var scalarPointsAtReleased = appointment.TreatmentPlanItemId.HasValue
                && released.Contains(appointment.TreatmentPlanItemId.Value);

            if (!pointsAtReleased && !scalarPointsAtReleased)
            {
                continue;
            }

            /*
             * A visit that is already cancelled or a no-show books nothing, so there is no slot to free —
             * but its dangling links are cleared, because nothing else ever will. The rows are KEPT (the
             * visit's acts are a record of what was planned) and only the plan pointers are dropped, which
             * is what `SetProcedures` re-derives the scalar from.
             */
            if (!TreatmentPlanWorkflowProjection.IsLive(appointment.Status))
            {
                appointment.SetProcedures(appointment.Procedures
                    .Select(p => new AppointmentProcedureInput(
                        p.ProcedureTypeId,
                        p.ProcedureName,
                        p.DurationMinutes,
                        p.ColorHex,
                        p.AgreedCost,
                        p.TreatmentPlanItemId.HasValue && released.Contains(p.TreatmentPlanItemId.Value)
                            ? null
                            : p.TreatmentPlanItemId,
                        p.TreatmentPlanItemId.HasValue && released.Contains(p.TreatmentPlanItemId.Value)
                            ? null
                            : p.TreatmentPlanItemStepId))
                    .ToList());
                await appointmentRepository.UpdateAsync(appointment, cancellationToken);
                continue;
            }

            var kept = appointment.Procedures
                .Where(p => !(p.TreatmentPlanItemId.HasValue && released.Contains(p.TreatmentPlanItemId.Value)))
                .Select(p => new AppointmentProcedureInput(
                    p.ProcedureTypeId,
                    p.ProcedureName,
                    p.DurationMinutes,
                    p.ColorHex,
                    p.AgreedCost,
                    p.TreatmentPlanItemId,
                    p.TreatmentPlanItemStepId))
                .ToList();

            if (kept.Count == 0)
            {
                toCancel.Add(appointment.Id);
                continue;
            }

            if (kept.Count == appointment.Procedures.Count && !scalarPointsAtReleased)
            {
                continue;
            }

            appointment.SetProcedures(kept);
            await appointmentRepository.UpdateAsync(appointment, cancellationToken);
        }

        return toCancel;
    }
}
