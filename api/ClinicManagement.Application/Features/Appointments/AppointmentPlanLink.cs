using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>Shared validation for linking an appointment to a treatment-plan step (create + update).</summary>
public static class AppointmentPlanLink
{
    /// <summary>Confirm the plan item exists and belongs to the given clinic + patient before it is linked.</summary>
    public static async Task<Result> ValidateAsync(
        ITreatmentPlanRepository treatmentPlanRepository,
        Guid? treatmentPlanId,
        Guid treatmentPlanItemId,
        Guid clinicId,
        Guid? patientId,
        CancellationToken cancellationToken)
    {
        if (!patientId.HasValue)
        {
            return Result.Failure("Un rendez-vous sans patient ne peut pas être lié à un acte du plan.");
        }
        if (!treatmentPlanId.HasValue)
        {
            return Result.Failure("Le plan de traitement est requis pour lier l'acte.");
        }

        var plan = await treatmentPlanRepository.GetByIdAsync(treatmentPlanId.Value, cancellationToken);
        if (plan == null || plan.ClinicId != clinicId || plan.PatientId != patientId.Value)
        {
            return Result.Failure("Plan de traitement introuvable.");
        }
        if (plan.Items.All(i => i.Id != treatmentPlanItemId))
        {
            return Result.Failure("Acte du plan introuvable.");
        }

        return Result.Success();
    }
}
