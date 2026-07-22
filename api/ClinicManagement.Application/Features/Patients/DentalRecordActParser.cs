using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>Shared parsing/validation of dental-record act inputs + odontogram-entry building (Create + Update).</summary>
public static class DentalRecordActParser
{
    public sealed record ParsedAct(DentalActInput Input, ToothCondition? Condition);

    /// <summary>Validate the acts (name, teeth dentition, condition) and parse each act's resulting condition.</summary>
    public static Result<List<ParsedAct>> Parse(IReadOnlyList<DentalActInput> acts, bool isAdultTeeth)
    {
        var result = new List<ParsedAct>();

        foreach (var a in acts)
        {
            if (string.IsNullOrWhiteSpace(a.ProcedureName))
            {
                return Result<List<ParsedAct>>.Failure("Le nom de l'acte est requis.");
            }

            foreach (var tooth in a.ToothNumbers)
            {
                if (DentalRecordTooth.IsAdultTooth(tooth) != isAdultTeeth)
                {
                    return Result<List<ParsedAct>>.Failure(
                        $"La dent {tooth} ne correspond pas à la dentition sélectionnée ({(isAdultTeeth ? "adulte" : "enfant")}).");
                }
            }

            ToothCondition? condition = null;
            if (!string.IsNullOrWhiteSpace(a.ResultingCondition))
            {
                if (!Enum.TryParse<ToothCondition>(a.ResultingCondition, ignoreCase: true, out var parsed))
                {
                    return Result<List<ParsedAct>>.Failure("État de dent invalide.");
                }
                condition = parsed;
            }

            result.Add(new ParsedAct(a, condition));
        }

        return Result<List<ParsedAct>>.Success(result);
    }

    /// <summary>Build the odontogram entries (one per act × tooth) for acts that produce a real tooth state.</summary>
    public static IEnumerable<ToothState> BuildToothStates(
        IReadOnlyList<ParsedAct> parsed, Guid patientId, DateTime treatmentDate, Guid dentalRecordId)
    {
        foreach (var p in parsed)
        {
            if (p.Condition is null or ToothCondition.Sain)
            {
                continue;
            }

            foreach (var tooth in p.Input.ToothNumbers.Distinct())
            {
                yield return new ToothState(
                    Guid.NewGuid(), patientId, tooth, p.Condition.Value, treatmentDate,
                    p.Input.Surfaces, p.Input.Note, dentalRecordId);
            }
        }
    }
}
