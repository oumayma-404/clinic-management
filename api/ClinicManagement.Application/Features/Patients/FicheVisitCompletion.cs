using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>What saving a fiche did to the visit it documents.</summary>
public enum FicheVisitOutcome
{
    /// <summary>The fiche documents no visit of this clinic.</summary>
    NoVisit,

    /// <summary>The séance is still running and nobody pressed « Terminer la séance »: the visit stays as it is.</summary>
    LeftRunning,

    /// <summary>The visit is now « Terminé ».</summary>
    Completed,

    /// <summary>The visit was already « Terminé ».</summary>
    AlreadyCompleted,

    /// <summary>The visit is cancelled or missed; it is left as it is (see <see cref="Appointment.MarkVisitCompleted"/>).</summary>
    Contradicted,
}

/// <summary>
/// When a fiche de soins makes its visit « Terminé » — the one rule, for the create command and the update command.
///
/// <para><b>A saved fiche is evidence the patient came, and it is not evidence the séance is over.</b> The create path
/// used to complete the visit on its first save, with no time check — harmless while a fiche was only ever written
/// after the patient had left, and wrong the moment a dentist fills it as she works: a save at 10:15 marked a
/// 10:00–11:00 visit « Terminé » with the patient still in the chair, and « Au fauteuil » showed the chair empty.
/// So the visit is completed when the dentist says so (« Terminer la séance » sends <c>finishSeance</c>) or when the
/// séance is over by the clock (<see cref="Appointment.HasEndedBy"/>, which counts a recorded retard). In between it
/// keeps its status, and <c>AppointmentProgressJob</c> completes it at its end when a fiche exists.</para>
///
/// <para>⚠️ The post-visit review is still withdrawn on <b>create</b>, whatever happens to the status: its only
/// question is « remplir la fiche ? », and the fiche now exists.</para>
///
/// <para>Best-effort, like every post-commit side effect: it never throws and never undoes the committed fiche.</para>
/// </summary>
public static class FicheVisitCompletion
{
    /// <summary>
    /// The rule itself, pure so its truth table is testable without a repository.
    ///
    /// <para>⚠️ <b>A patient « Au fauteuil » is never ended by the clock</b> (`salle-attente` round 2, C1): a séance
    /// called at 10:25 for a 10:00 RDV is not over at 10:30, and a fiche saved at 10:35 used to mark it « Terminé »
    /// with the patient still seated and the chair shown empty. While the board says he is in the chair, only
    /// « Terminer la séance », « Patient suivant », « Terminer » or a payment end the visit.</para>
    /// </summary>
    public static bool ShouldComplete(Appointment visit, bool finishSeance, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(visit);
        return finishSeance
               || (visit.WaitingRoomLane != WaitingRoomLane.InChair && visit.HasEndedBy(nowUtc));
    }

    /// <summary>
    /// Applies <see cref="ShouldComplete"/> to the visit <paramref name="appointmentId"/> names, after the fiche has
    /// been committed.
    /// </summary>
    /// <param name="isCreate">True on the fiche's first save — the one that withdraws the post-visit review.</param>
    public static async Task<FicheVisitOutcome> ApplyAsync(
        Guid appointmentId,
        Guid clinicId,
        bool finishSeance,
        bool isCreate,
        DateTime nowUtc,
        IAppointmentRepository appointments,
        IUnitOfWork unitOfWork,
        INotificationGenerator notifications,
        IRealtimeNotifier realtime,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var appointment = await appointments.GetByIdAsync(appointmentId, cancellationToken);
            if (appointment == null || appointment.ClinicId != clinicId)
            {
                return FicheVisitOutcome.NoVisit; // cross-clinic or unknown id → leave everything unchanged
            }

            var outcome = FicheVisitOutcome.LeftRunning;
            if (ShouldComplete(appointment, finishSeance, nowUtc))
            {
                // The salle d'attente's chair ends with the séance (`salle-attente` S3). Read BEFORE the status moves:
                // the lane is derived from it, and a completed visit is no longer « au fauteuil ».
                var leftChair = appointment.StampLeftChairIfOpen(nowUtc);

                // Three outcomes, not two (AC-P1.12): `Contradicted` is a fiche documenting a visit the schedule says
                // was cancelled or missed. It is logged and the visit is deliberately NOT reopened.
                var completion = appointment.MarkVisitCompleted();
                outcome = completion switch
                {
                    VisitCompletionOutcome.Completed => FicheVisitOutcome.Completed,
                    VisitCompletionOutcome.AlreadyCompleted => FicheVisitOutcome.AlreadyCompleted,
                    _ => FicheVisitOutcome.Contradicted,
                };

                if (outcome == FicheVisitOutcome.Contradicted)
                {
                    logger.LogWarning(
                        "Fiche de soins saved against appointment {AppointmentId}, which is {Status}. The "
                        + "appointment was left unchanged.",
                        appointmentId, appointment.Status);
                }

                // Only Completed changed the row; the save is skipped otherwise rather than issuing an empty UPDATE.
                if (outcome == FicheVisitOutcome.Completed || leftChair)
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
            }

            // The review asks « remplir la fiche ? » — answered by the fiche's existence, so its first save withdraws
            // it, and a later save that closes the visit withdraws it too (idempotent).
            if (isCreate || outcome != FicheVisitOutcome.LeftRunning)
            {
                await notifications.CancelPostVisitReviewAsync(appointment.ClinicId, appointmentId, cancellationToken);
            }

            // The fiche commands broadcast « patients »; the agenda listens to « appointments ».
            await realtime.NotifyEntityChangedAsync(appointment.ClinicId, "appointments", cancellationToken);

            return outcome;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Post-save visit completion failed for appointment {AppointmentId}", appointmentId);
            return FicheVisitOutcome.NoVisit;
        }
    }
}
