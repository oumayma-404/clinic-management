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
    /// Mark the given plan step "réalisé", linked to <paramref name="dentalRecordId"/> (AC-4). Returns a
    /// failure for a missing/cross-tenant plan or unknown item; the plan's own guard (must be accepted/in
    /// progress) surfaces as an <see cref="InvalidOperationException"/> for the caller to translate.
    /// </summary>
    public static async Task<Result> LinkPlanItemAsync(
        ITreatmentPlanRepository treatmentPlanRepository,
        Guid? treatmentPlanId,
        Guid treatmentPlanItemId,
        Guid patientId,
        Guid clinicId,
        Guid dentalRecordId,
        DateTime doneOn,
        CancellationToken cancellationToken)
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

        plan.MarkItemDone(treatmentPlanItemId, doneOn, dentalRecordId);
        await treatmentPlanRepository.UpdateAsync(plan, cancellationToken);
        return Result.Success();
    }
}
