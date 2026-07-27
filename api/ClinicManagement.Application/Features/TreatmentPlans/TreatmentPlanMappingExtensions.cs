using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>Maps <see cref="TreatmentPlan"/> aggregates to their DTOs.</summary>
public static class TreatmentPlanMappingExtensions
{
    /// <summary>
    /// Map without the derived read-back — the shape command handlers return. Scheduling and devis→facture
    /// fields stay null here: the frontend reloads after every mutation (and the realtime broadcast triggers a
    /// refetch anyway), so threading two repositories through every command handler would buy nothing.
    /// </summary>
    public static TreatmentPlanDto ToDto(this TreatmentPlan plan, string? patientName = null)
        => plan.ToDto(patientName, TreatmentPlanWorkflow.Empty);

    /// <summary>
    /// Map with the derived scheduling + devis→facture read-back (the query paths). Clinical progress
    /// (<c>ItemsDone</c>/<c>ItemsTotal</c>) needs no lookup and is always populated.
    /// </summary>
    public static TreatmentPlanDto ToDto(
        this TreatmentPlan plan,
        string? patientName,
        TreatmentPlanWorkflow workflow)
    {
        var hasInvoice = workflow.InvoiceByPlanId.TryGetValue(plan.Id, out var invoice);
        workflow.NextAppointmentAtByPlanId.TryGetValue(plan.Id, out var nextAppointmentAt);

        return new TreatmentPlanDto
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
        Version = plan.Version,
            UpdatedAt = plan.UpdatedAt,
            RevisionNumber = plan.RevisionNumber,
            ItemsDone = plan.Items.Count(i => i.Status == TreatmentPlanItemStatus.Done),
            ItemsTotal = plan.Items.Count,
            NextAppointmentAt = nextAppointmentAt,
            LinkedInvoiceId = hasInvoice ? invoice.InvoiceId : null,
            LinkedInvoiceNumber = hasInvoice ? invoice.Number : null,
            LinkedInvoiceStatus = hasInvoice ? invoice.Status.ToString() : null,
            Items = plan.Items
                .Select(i => ToItemDto(i, workflow))
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
                    LastPaidOn = i.LastPaidOn,
                    // Oldest first, with the insertion stamp as tiebreaker — two payments on the same day
                    // are common and the list needs a deterministic order.
                    Payments = i.Payments
                        .OrderBy(p => p.PaidOn)
                        .ThenBy(p => p.CreatedAt)
                        .Select(p => new InstallmentPaymentDto
                        {
                            Id = p.Id,
                            Amount = p.Amount,
                            Method = p.Method.ToString(),
                            PaidOn = p.PaidOn,
                            CreatedAt = p.CreatedAt,
                            IsVoided = p.IsVoided,
                            VoidedAt = p.VoidedAt,
                            VoidReason = p.VoidReason,
                            VoidedByName = p.VoidedByName
                        })
                        .ToList()
                })
                .ToList()
        };
    }

    private static TreatmentPlanItemDto ToItemDto(TreatmentPlanItem item, TreatmentPlanWorkflow workflow)
    {
        var hasAppointment = workflow.ScheduledByItemId.TryGetValue(item.Id, out var appointment);

        return new TreatmentPlanItemDto
        {
            Id = item.Id,
            DentalActCodeId = item.DentalActCodeId,
            CodeActe = item.CodeActe,
            ProcedureTypeId = item.ProcedureTypeId,
            DesignationFr = item.DesignationFr,
            ToothNumbers = item.ToothNumbers.ToList(),
            PlannedCost = item.PlannedCost,
            Status = item.Status.ToString(),
            DoneDate = item.DoneDate,
            LinkedDentalRecordId = item.LinkedDentalRecordId,
            SequenceNumber = item.SequenceNumber,
            ScheduledAppointmentId = hasAppointment ? appointment!.Id : null,
            ScheduledAt = hasAppointment ? appointment!.AppointmentDateTime : null,
            ScheduledAppointmentStatus = hasAppointment ? appointment!.Status.ToString() : null
        };
    }
}
