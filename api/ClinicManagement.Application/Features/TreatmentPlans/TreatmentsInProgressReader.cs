using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// Assembles « Traitements en cours »: the acts a cabinet has started and not finished, with the next step and
/// whether it is booked.
/// <para>
/// A shared reader rather than a handler body, for <c>VisitClosureReader</c>'s stated reason: the worklist page
/// and the journée's own count are two callers, and they must not be able to disagree about what counts. A chip
/// reading « 4 » that opens a list of 6 is worse than no chip.
/// </para>
/// <para>
/// Three reads, all batched: the paged projection, one appointment read over the page's acts, one patient read
/// over the page's patients. Never per row.
/// </para>
/// </summary>
public static class TreatmentsInProgressReader
{
    public static async Task<PagedResult<TreatmentInProgressDto>> ReadAsync(
        Guid clinicId,
        PageRequest? paging,
        ITreatmentPlanRepository planRepository,
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        CancellationToken cancellationToken)
    {
        var page = await planRepository.GetTreatmentsInProgressAsync(clinicId, paging, cancellationToken);
        if (page.Items.Count == 0)
        {
            return page.Map(_ => new TreatmentInProgressDto());
        }

        // Which of the page's next steps already have a séance. Asked over the acts rather than the steps
        // because GetByTreatmentPlanItemIdsAsync already exists and is already indexed — a step-keyed repository
        // method would be a second read of the same rows for the same answer.
        var itemIds = page.Items.Select(f => f.ItemId).Distinct().ToList();
        var appointments = await appointmentRepository.GetByTreatmentPlanItemIdsAsync(
            clinicId, itemIds, cancellationToken);

        // TreatmentPlanWorkflowProjection.IsLive is THE rule for « does this appointment still book anything »,
        // asked here rather than re-stated: a cancelled or missed séance must return its step to « à planifier »
        // on this screen exactly as it does on the devis, or the two would disagree about the same visit.
        var bookedStep = appointments
            .Where(a => TreatmentPlanWorkflowProjection.IsLive(a.Status))
            .SelectMany(a => a.LinkedTreatmentPlanItemStepIds.Select(stepId => (StepId: stepId, Appointment: a)))
            .GroupBy(x => x.StepId)
            .ToDictionary(
                g => g.Key,
                // Earliest booking for the step — the one the practice will keep.
                g => g.OrderBy(x => x.Appointment.AppointmentDateTime).First().Appointment);

        var patients = await patientRepository.GetByIdsAsync(
            clinicId, page.Items.Select(f => f.PatientId).Distinct().ToList(), cancellationToken);

        return page.Map(fact =>
        {
            var hasBooking = fact.NextStepId.HasValue
                             && bookedStep.TryGetValue(fact.NextStepId.Value, out var appointment);
            var booking = hasBooking ? bookedStep[fact.NextStepId!.Value] : null;

            return new TreatmentInProgressDto
            {
                PlanId = fact.PlanId,
                PlanNumber = fact.PlanNumber,
                PatientId = fact.PatientId,
                PatientName = patients.TryGetValue(fact.PatientId, out var patient)
                    ? patient.GetFullName()
                    : null,
                ItemId = fact.ItemId,
                DesignationFr = fact.DesignationFr,
                StepsTotal = fact.StepsTotal,
                StepsDone = fact.StepsDone,
                NextStepId = fact.NextStepId,
                NextStepLabel = fact.NextStepLabel,
                // 1-based for the screen — « étape 3 sur 3 ». The stored rank is 0-based and stays that way.
                // ⚠️ `+ 1` because `SequenceNumber` is dense **0..n-1** — verify-schema's
                // `plan-step-sequence-dense` asserts exactly that — while « la 3e étape » is what a person
                // reads. Serving the raw column would name every step one too low, plausibly, for ever.
                NextStepNumber = fact.NextStepSequenceNumber + 1,
                NextStepEstimatedDurationMinutes = fact.NextStepEstimatedDurationMinutes,
                LastStepDoneOn = fact.LastStepDoneOn,
                // The protocol's interval applied to the previous séance's date — what lets the screen say
                // « pas encore due » instead of alarming at a flat fortnight on a treatment that is on time.
                NextStepDueFrom = fact.NextStepMinDaysAfterPrevious is int days && fact.LastStepDoneOn is DateTime last
                    ? last.Date.AddDays(days)
                    : null,
                NextStepAppointmentId = booking?.Id,
                NextStepAppointmentAt = booking?.AppointmentDateTime,
            };
        });
    }
}
