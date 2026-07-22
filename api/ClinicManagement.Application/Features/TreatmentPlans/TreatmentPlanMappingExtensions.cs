using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>Maps <see cref="TreatmentPlan"/> aggregates to their DTOs.</summary>
public static class TreatmentPlanMappingExtensions
{
    public static TreatmentPlanDto ToDto(this TreatmentPlan plan, string? patientName = null) => new()
    {
        Id = plan.Id,
        PatientId = plan.PatientId,
        PatientName = patientName,
        Number = plan.Number,
        Status = plan.Status.ToString(),
        Title = plan.Title,
        Notes = plan.Notes,
        AcceptedDate = plan.AcceptedDate,
        CancellationReason = plan.CancellationReason,
        TotalPlanned = plan.TotalPlanned,
        AmountPaid = plan.AmountPaid,
        Outstanding = plan.Outstanding,
        CreatedAt = plan.CreatedAt,
        UpdatedAt = plan.UpdatedAt,
        Items = plan.Items
            .Select(i => new TreatmentPlanItemDto
            {
                Id = i.Id,
                DentalActCodeId = i.DentalActCodeId,
                CodeActe = i.CodeActe,
                DesignationFr = i.DesignationFr,
                ToothNumbers = i.ToothNumbers.ToList(),
                PlannedCost = i.PlannedCost,
                Status = i.Status.ToString(),
                DoneDate = i.DoneDate,
                LinkedDentalRecordId = i.LinkedDentalRecordId
            })
            .ToList(),
        Installments = plan.Installments
            .OrderBy(i => i.DueDate)
            .Select(i => new InstallmentDto
            {
                Id = i.Id,
                DueDate = i.DueDate,
                Amount = i.Amount,
                AmountPaid = i.AmountPaid,
                Outstanding = i.Outstanding,
                IsPaid = i.IsPaid,
                LastMethod = i.LastMethod?.ToString(),
                LastPaidOn = i.LastPaidOn
            })
            .ToList()
    };
}
