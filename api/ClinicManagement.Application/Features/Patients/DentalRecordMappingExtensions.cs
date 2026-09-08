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
        AppointmentId = record.AppointmentId,
        InterventionDate = record.InterventionDate,
        ProcedureType = record.ProcedureType,
        Cost = record.Cost,
        AmountPaid = record.AmountPaid,
        PaymentMethod = record.PaymentMethod?.ToString(),
        ChequeNumber = record.ChequeNumber,
        ChequeBankName = record.ChequeBankName,
        ChequeDueDate = record.ChequeDueDate,
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
                UnitCost = a.UnitCost,
                IsPerTooth = a.IsPerTooth,
                ToothNumbers = a.ToothNumbers.ToList(),
                // ⚠️ Read back on purpose: `SetActs` rebuilds every act from the input, so a fiche reopened for
                // editing that could not see which tooth was a pontique would send the list back empty and
                // silently flatten the bridge on the next save.
                PonticToothNumbers = a.PonticToothNumbers.ToList(),
                // Same reason, second list: an editor blind to the implant piliers sends them back empty and
                // the next save charts rooted abutments over implants.
                ImplantPilierToothNumbers = a.ImplantPilierToothNumbers.ToList(),
                ResultingCondition = a.ResultingCondition?.ToString(),
                Surfaces = a.Surfaces,
                Note = a.Note
            })
            .ToList(),
        CreatedAt = record.CreatedAt,
        Version = record.Version,
        UpdatedAt = record.UpdatedAt
    };
}
