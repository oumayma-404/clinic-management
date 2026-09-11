using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

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

            /*
             * The pontique list is validated as FDI here and normalised (intersected with the act's teeth,
             * cleared when the act is not a bridge) by the aggregate — see `DentalRecordAct`'s constructor for
             * why that half is a fold and not a refusal. A tooth number that is not a tooth is a different
             * thing entirely and is refused, exactly as `ToothNumbers` is two loops above.
             */
            foreach (var tooth in a.PonticToothNumbers.Concat(a.ImplantPilierToothNumbers))
            {
                if (!FdiTooth.IsValid(tooth))
                {
                    return Result<List<DentalRecordActInput>>.Failure($"Numéro de dent invalide : {tooth}.");
                }
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
                a.Note,
                a.PonticToothNumbers,
                a.ImplantPilierToothNumbers,
                a.IsUnfinished));
        }

        return Result<List<DentalRecordActInput>>.Success(result);
    }

    /// <summary>Build the odontogram entries (one per act × tooth) for acts that produce a real tooth state.</summary>
    public static IEnumerable<ToothState> BuildToothStates(
        IReadOnlyList<DentalRecordActInput> acts,
        Guid patientId,
        Guid clinicId,
        DateTime treatmentDate,
        Guid dentalRecordId)
    {
        foreach (var a in acts)
        {
            if (a.ResultingCondition is null or ToothCondition.Sain)
            {
                continue;
            }

            /*
             * ⚠️ **One act, and possibly TWO states across its teeth** — the only case in the product where that
             * is true, and the reason `BridgeCharting` exists. A three-unit bridge is one act (« Couronne /
             * bridge (par élément) » priced per element, so one line and the right total) whose 14 and 16 are
             * piliers and whose 15 is a pontique. Charting the act's single `ResultingCondition` across all
             * three said « three abutments, no pontic »: a bridge that cannot exist, drawn on the patient's
             * chart with a travée joining it.
             *
             * With no pontique marked the fold returns the act's own condition unchanged, so every record
             * written before this — and every act that is not a bridge — charts exactly as it did.
             */
            var pontics = a.PonticToothNumbers ?? Array.Empty<int>();
            var implantPiliers = a.ImplantPilierToothNumbers ?? Array.Empty<int>();
            var teeth = a.ToothNumbers.Distinct().ToList();

            /*
             * ⚠️ **One bridge act, one group — and a ONE-TOOTH act gets NONE.** The group is what tells two
             * bridges placed side by side apart, so it has to be minted here (the only place that knows which
             * teeth arrived together) rather than derived from the chart later.
             *
             * ⚠️ The `>= 2` is not a tidiness rule, it is a **regression guard**. Before
             * `PonticToothNumbers` existed the only way to record a three-unit bridge was the SAME PROCEDURE
             * TWICE — `DentalRecordAct.ImplantPilierToothNumbers`' sibling doc records that — so fiches are
             * still entered as two acts of one tooth each. Give each of those a group and every one becomes a
             * group of one, both are then excluded from `bridge-runs.ts`' ungrouped fallback, and the travée
             * simply disappears from a workflow people use. A one-tooth act asserts nothing about a span, so it
             * must not claim one: null leaves it to the adjacency scan, exactly as today.
             *
             * A re-save mints a fresh id, which is safe because `UpdateDentalRecordCommand` deletes and
             * rebuilds this fiche's whole set of states in one transaction.
             */
            var bridgeGroupId = teeth.Count >= 2 && BridgeCharting.IsUnit(a.ResultingCondition.Value)
                ? Guid.NewGuid()
                : (Guid?)null;

            foreach (var tooth in teeth)
            {
                var condition = BridgeCharting.ConditionFor(
                    a.ResultingCondition.Value, pontics, implantPiliers, tooth);
                yield return new ToothState(
                    Guid.NewGuid(), patientId, clinicId, tooth, condition, treatmentDate,
                    a.Surfaces, a.Note, dentalRecordId, bridgeGroupId: bridgeGroupId);
            }
        }
    }
}
