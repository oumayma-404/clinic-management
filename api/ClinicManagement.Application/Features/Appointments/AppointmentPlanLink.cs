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

    /// <summary>
    /// Confirm that <b>every</b> step of a grouped séance belongs to the same plan, clinic and patient — one plan
    /// load for the whole set.
    /// <para>
    /// A batch rather than a loop over <see cref="ValidateAsync"/> because grouping is the normal case now (« ces
    /// trois actes en une séance »), and per-item validation would re-read the same aggregate once per act. It also
    /// makes the shared-plan rule explicit: the acts of one séance must come from one devis, since the appointment
    /// carries a single <c>TreatmentPlanId</c> and a mixed set would have no coherent one to record.
    /// </para>
    /// </summary>
    /// <returns>
    /// The validated steps' <b>désignations</b>, keyed by id. Returned rather than discarded because a plan act
    /// with no catalog procedure behind it (a hand-typed devis line) still needs a name on the appointment row
    /// that carries its link — and the plan aggregate is already loaded here, so fetching it again downstream
    /// would be a second read of the same rows.
    /// </returns>
    public static async Task<Result<Dictionary<Guid, string>>> ValidateManyAsync(
        ITreatmentPlanRepository treatmentPlanRepository,
        Guid? treatmentPlanId,
        IReadOnlyCollection<Guid> treatmentPlanItemIds,
        Guid clinicId,
        Guid? patientId,
        CancellationToken cancellationToken)
    {
        if (treatmentPlanItemIds.Count == 0)
        {
            return Result<Dictionary<Guid, string>>.Success(new Dictionary<Guid, string>());
        }
        if (!patientId.HasValue)
        {
            return Result<Dictionary<Guid, string>>.Failure(
                "Un rendez-vous sans patient ne peut pas être lié à un acte du plan.");
        }
        if (!treatmentPlanId.HasValue)
        {
            return Result<Dictionary<Guid, string>>.Failure("Le plan de traitement est requis pour lier l'acte.");
        }

        var plan = await treatmentPlanRepository.GetByIdAsync(treatmentPlanId.Value, cancellationToken);
        if (plan == null || plan.ClinicId != clinicId || plan.PatientId != patientId.Value)
        {
            return Result<Dictionary<Guid, string>>.Failure("Plan de traitement introuvable.");
        }

        var byId = plan.Items.ToDictionary(i => i.Id, i => i.DesignationFr);
        if (treatmentPlanItemIds.Any(id => !byId.ContainsKey(id)))
        {
            return Result<Dictionary<Guid, string>>.Failure("Acte du plan introuvable.");
        }

        return Result<Dictionary<Guid, string>>.Success(
            treatmentPlanItemIds.ToDictionary(id => id, id => byId[id]));
    }
}
