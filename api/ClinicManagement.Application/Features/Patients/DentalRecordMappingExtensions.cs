using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>Maps <see cref="DentalRecord"/> aggregates to their DTOs (incl. the acts + derived fields).</summary>
public static class DentalRecordMappingExtensions
{
    public static DentalRecordDto ToDto(this DentalRecord record) => new()
    {
        Id = record.Id,
        PatientId = record.PatientId,
        InterventionDate = record.InterventionDate,
        ProcedureType = record.ProcedureType,
        Cost = record.Cost,
        AmountPaid = record.AmountPaid,
        Balance = record.Cost - record.AmountPaid,
        Notes = record.Notes.ToList(),
        ImportantNotes = record.ImportantNotes.ToList(),
        IsAdultTeeth = record.IsAdultTeeth,
        ToothNumbers = record.Teeth.Select(t => t.ToothNumber).OrderBy(t => t).ToList(),
        Acts = record.Acts
            .Select(a => new DentalRecordActDto
            {
                Id = a.Id,
                ProcedureTypeId = a.ProcedureTypeId,
                ProcedureName = a.ProcedureName,
                Cost = a.Cost,
                ToothNumbers = a.ToothNumbers.ToList(),
                ResultingCondition = a.ResultingCondition?.ToString(),
                Surfaces = a.Surfaces,
                Note = a.Note
            })
            .ToList(),
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };
}
