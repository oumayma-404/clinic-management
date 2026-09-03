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
    /// Confirm that <b>every</b> act of a grouped séance belongs to the same plan, clinic and patient — and that
    /// every named step belongs to the act it is named with. One plan load for the whole set.
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
        IReadOnlyCollection<(Guid ItemId, Guid? StepId)> treatmentPlanLinks,
        Guid clinicId,
        Guid? patientId,
        CancellationToken cancellationToken)
    {
        if (treatmentPlanLinks.Count == 0)
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

        var byId = plan.Items.ToDictionary(i => i.Id, i => i);
        if (treatmentPlanLinks.Any(l => !byId.ContainsKey(l.ItemId)))
        {
            return Result<Dictionary<Guid, string>>.Failure("Acte du plan introuvable.");
        }

        // ⚠️ A step is validated against the step list of ITS OWN act, not against the plan's steps as a set.
        // Both checks pass for a step id that exists on a different line of the same devis, and that request is
        // exactly what a stale browser sends after the acts are reordered or amended — it would book « le
        // scellement » of one act as a séance of another, and every screen would then read the progress of the
        // wrong bridge. The plan aggregate is already loaded here, so this costs nothing.
        foreach (var link in treatmentPlanLinks)
        {
            if (link.StepId.HasValue && byId[link.ItemId].Steps.All(s => s.Id != link.StepId.Value))
            {
                return Result<Dictionary<Guid, string>>.Failure("Étape du devis introuvable.");
            }
        }

        return Result<Dictionary<Guid, string>>.Success(
            treatmentPlanLinks
                .Select(l => l.ItemId)
                .Distinct()
                .ToDictionary(id => id, id => byId[id].DesignationFr));
    }
}
