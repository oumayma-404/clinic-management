using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>Shared parsing/validation of dental-record act inputs + odontogram-entry building (Create + Update).</summary>
public static class DentalRecordActParser
{
    /// <summary>
    /// Validate the requested acts (name, cost, FDI tooth numbers, resulting condition) and turn each into the
    /// <see cref="DentalRecordActInput"/> the aggregate consumes.
    /// A session is NOT restricted to a single dentition: each tooth only has to be a valid FDI number, so a
    /// mixed-dentition visit (a permanent 36 alongside a deciduous 75) is recordable. The record's
    /// <c>IsAdultTeeth</c> flag is a display hint, not a constraint.
    /// </summary>
    public static Result<List<DentalRecordActInput>> Parse(IReadOnlyList<DentalActInput> acts)
    {
        var result = new List<DentalRecordActInput>();

        foreach (var a in acts)
        {
            if (string.IsNullOrWhiteSpace(a.ProcedureName))
            {
                return Result<List<DentalRecordActInput>>.Failure("Le nom de l'acte est requis.");
            }

            if (a.Cost < 0)
            {
                return Result<List<DentalRecordActInput>>.Failure("Le coût de l'acte ne peut pas être négatif.");
            }

            if (a.UnitCost < 0)
            {
                return Result<List<DentalRecordActInput>>.Failure("Le prix unitaire de l'acte ne peut pas être négatif.");
            }

            foreach (var tooth in a.ToothNumbers)
            {
                if (!FdiTooth.IsValid(tooth))
                {
                    return Result<List<DentalRecordActInput>>.Failure($"Numéro de dent invalide : {tooth}.");
                }
            }

            ToothCondition? condition = null;
            if (!string.IsNullOrWhiteSpace(a.ResultingCondition))
            {
                if (!Enum.TryParse<ToothCondition>(a.ResultingCondition, ignoreCase: true, out var parsed))
                {
                    return Result<List<DentalRecordActInput>>.Failure("État de dent invalide.");
                }
                condition = parsed;
            }

            result.Add(new DentalRecordActInput(
                a.ProcedureTypeId,
                a.ProcedureName,
                a.Cost,
                a.UnitCost,
                a.IsPerTooth,
                a.ToothNumbers,
                condition,
                a.Surfaces,
                a.Note));
        }

        return Result<List<DentalRecordActInput>>.Success(result);
    }

    /// <summary>Build the odontogram entries (one per act × tooth) for acts that produce a real tooth state.</summary>
    public static IEnumerable<ToothState> BuildToothStates(
        IReadOnlyList<DentalRecordActInput> acts, Guid patientId, DateTime treatmentDate, Guid dentalRecordId)
    {
        foreach (var a in acts)
        {
            if (a.ResultingCondition is null or ToothCondition.Sain)
            {
                continue;
            }

            foreach (var tooth in a.ToothNumbers.Distinct())
            {
                yield return new ToothState(
                    Guid.NewGuid(), patientId, tooth, a.ResultingCondition.Value, treatmentDate,
                    a.Surfaces, a.Note, dentalRecordId);
            }
        }
    }
}
