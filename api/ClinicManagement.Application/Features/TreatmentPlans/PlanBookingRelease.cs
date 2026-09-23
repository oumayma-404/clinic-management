using ClinicManagement.Domain.Entities;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

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
                appointment.SetProcedures(StripLinks(appointment, released));
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
                // ⚠️ Links cleared BEFORE the cancellation, never left on it: the class's own rule is that a
                // cancelled visit must not point at a released act, « because nothing else ever will » — and
                // after a reassignment the act is on another patient's devis.
                appointment.SetProcedures(StripLinks(appointment, released));
                await appointmentRepository.UpdateAsync(appointment, cancellationToken);

                // ⚠️ Only a visit still AHEAD is cancelled. One whose slot has started or passed (« En cours »,
                // « Séance passée ») happened or may have — cancelling it would count it as an absence, so it
                // keeps its act and just stops claiming the devis.
                if (appointment.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed)
                {
                    toCancel.Add(appointment.Id);
                }
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

    /// <summary>
    /// The visit's rows with every link to a released act dropped — the acts KEPT, since they record what was
    /// planned, and each row's price carried across because the list is replace-semantics.
    /// </summary>
    private static List<AppointmentProcedureInput> StripLinks(Appointment appointment, HashSet<Guid> released) =>
        appointment.Procedures
            .Select(p =>
            {
                var drop = p.TreatmentPlanItemId.HasValue && released.Contains(p.TreatmentPlanItemId.Value);
                return new AppointmentProcedureInput(
                    p.ProcedureTypeId,
                    p.ProcedureName,
                    p.DurationMinutes,
                    p.ColorHex,
                    p.AgreedCost,
                    drop ? null : p.TreatmentPlanItemId,
                    drop ? null : p.TreatmentPlanItemStepId);
            })
            // A link-only row (a hand-typed devis line) has nothing left once its link goes.
            .Where(p => p.ProcedureTypeId.HasValue || p.TreatmentPlanItemId.HasValue)
            .ToList();

    /// <summary>
    /// Let go of the visits booked for <b>steps</b> that no longer exist on their act — the protocol was re-cut
    /// with a booked séance in it. The visit keeps its act and its link to the act; only the dead step goes, so
    /// it books « the next séance » of the same treatment instead of pointing at nothing (which refused both the
    /// visit's next save and its fiche with « Étape du devis introuvable »).
    /// </summary>
    public static async Task ReleaseStepsAsync(
        Guid itemId,
        IReadOnlyCollection<Guid> releasedStepIds,
        Guid clinicId,
        IAppointmentRepository appointmentRepository,
        CancellationToken cancellationToken)
    {
        if (releasedStepIds.Count == 0)
        {
            return;
        }

        var released = releasedStepIds.ToHashSet();
        var appointments = await appointmentRepository.GetByTreatmentPlanItemIdsAsync(
            clinicId, new List<Guid> { itemId }, cancellationToken);
        foreach (var appointment in appointments)
        {
            if (appointment.Status == AppointmentStatus.Completed
                || !appointment.Procedures.Any(p => p.TreatmentPlanItemStepId.HasValue
                                                   && released.Contains(p.TreatmentPlanItemStepId.Value)))
            {
                continue;
            }

            // Two rows of one act that both lose their step would collapse into « the act twice », which
            // SetProcedures refuses — so the act is kept once, and not at all beside a surviving step of it.
            var rows = new List<AppointmentProcedureInput>();
            var wholeActKept = new HashSet<Guid>();
            foreach (var p in appointment.Procedures)
            {
                var deadStep = p.TreatmentPlanItemStepId.HasValue
                               && released.Contains(p.TreatmentPlanItemStepId.Value);
                if (!deadStep)
                {
                    rows.Add(new AppointmentProcedureInput(
                        p.ProcedureTypeId, p.ProcedureName, p.DurationMinutes, p.ColorHex, p.AgreedCost,
                        p.TreatmentPlanItemId, p.TreatmentPlanItemStepId));
                    continue;
                }

                var survivingStepOfSameAct = appointment.Procedures.Any(o =>
                    !ReferenceEquals(o, p) && o.TreatmentPlanItemId == p.TreatmentPlanItemId
                    && o.TreatmentPlanItemStepId.HasValue && !released.Contains(o.TreatmentPlanItemStepId.Value));
                if (survivingStepOfSameAct || !wholeActKept.Add(p.TreatmentPlanItemId!.Value))
                {
                    continue;
                }

                rows.Add(new AppointmentProcedureInput(
                    p.ProcedureTypeId, p.ProcedureName, p.DurationMinutes, p.ColorHex, p.AgreedCost,
                    p.TreatmentPlanItemId, null));
            }

            appointment.SetProcedures(rows);
            await appointmentRepository.UpdateAsync(appointment, cancellationToken);
        }
    }

    /// <summary>
    /// The fiche recorded at this visit no longer carries these devis acts, so the visit stops claiming them too —
    /// its acts kept, only the links dropped, whatever its status (it is usually « Terminé » by then: the fiche
    /// completed it). Left in place, the devis read « séance passée, fiche à enregistrer » for ever on a séance
    /// whose fiche exists and says it did something else.
    /// </summary>
    public static async Task DetachVisitAsync(
        Guid? appointmentId,
        IReadOnlyCollection<Guid> itemIds,
        Guid clinicId,
        IAppointmentRepository appointmentRepository,
        CancellationToken cancellationToken)
    {
        if (!appointmentId.HasValue || itemIds.Count == 0)
        {
            return;
        }

        var appointment = await appointmentRepository.GetByIdAsync(appointmentId.Value, cancellationToken);
        var released = itemIds.ToHashSet();
        if (appointment == null || appointment.ClinicId != clinicId
            || !appointment.Procedures.Any(p => p.TreatmentPlanItemId.HasValue
                                               && released.Contains(p.TreatmentPlanItemId.Value)))
        {
            return;
        }

        appointment.SetProcedures(StripLinks(appointment, released));
        await appointmentRepository.UpdateAsync(appointment, cancellationToken);
    }

    /// <summary>
    /// A devis line now names another catalogue act: every visit still ahead that books it takes the new act's
    /// name, colour and length on the row carrying the link. A visit that already happened keeps what it was.
    /// </summary>
    public static async Task FollowActChangeAsync(
        Guid itemId,
        ProcedureType procedureType,
        Guid clinicId,
        IAppointmentRepository appointmentRepository,
        CancellationToken cancellationToken)
    {
        var appointments = await appointmentRepository.GetByTreatmentPlanItemIdsAsync(
            clinicId, new List<Guid> { itemId }, cancellationToken);
        foreach (var appointment in appointments.Where(a => TreatmentPlanWorkflowProjection.IsLive(a.Status)
                                                            && a.Status != AppointmentStatus.Completed))
        {
            if (!appointment.Procedures.Any(p => p.TreatmentPlanItemId == itemId
                                                 && p.ProcedureTypeId != procedureType.Id))
            {
                continue;
            }

            appointment.SetProcedures(appointment.Procedures
                .Select(p => p.TreatmentPlanItemId == itemId
                    ? new AppointmentProcedureInput(
                        procedureType.Id, procedureType.Name, procedureType.DefaultDurationMinutes,
                        procedureType.Color.Value, p.AgreedCost, p.TreatmentPlanItemId, p.TreatmentPlanItemStepId)
                    : new AppointmentProcedureInput(
                        p.ProcedureTypeId, p.ProcedureName, p.DurationMinutes, p.ColorHex, p.AgreedCost,
                        p.TreatmentPlanItemId, p.TreatmentPlanItemStepId))
                .ToList());
            await appointmentRepository.UpdateAsync(appointment, cancellationToken);
        }
    }

    /// <summary>
    /// Cancel the visits <see cref="ReleaseAsync"/> left with nothing to do — AFTER the plan is saved, and through
    /// <c>UpdateAppointmentCommand</c> so the reminders, the Google event, the notification and the realtime
    /// broadcast all follow (see <c>AmendTreatmentPlanCommand</c> for both reasons). A failure is logged, never
    /// fatal: the act is already gone and a slot still booked is the direction a dentist can see and fix.
    /// </summary>
    public static async Task CancelEmptiedAsync(
        ISender sender,
        IEnumerable<Guid> appointmentIds,
        string reason,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var appointmentId in appointmentIds)
        {
            var cancelled = await sender.Send(
                new UpdateAppointmentCommand
                {
                    Id = appointmentId,
                    Status = nameof(AppointmentStatus.Cancelled),
                    CancellationReason = reason,
                },
                cancellationToken);
            if (!cancelled.IsSuccess)
            {
                logger.LogWarning(
                    "Could not cancel appointment {AppointmentId} released from its devis: {Error}",
                    appointmentId, cancelled.Error);
            }
        }
    }
}
