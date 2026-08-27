using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Invoices;

/// <summary>Maps <see cref="Invoice"/> aggregates to their DTOs.</summary>
public static class InvoiceMappingExtensions
{
    /// <param name="creditNotes">
    /// The avoirs established against this invoice. They live in their own aggregate with only a soft
    /// <c>InvoiceId</c> back-link, so the caller has to read them and hand them in — passing null yields a
    /// credited total of 0, which is correct for a caller that genuinely has no avoirs to show.
    /// </param>
    public static InvoiceDto ToDto(
        this Invoice invoice,
        string? patientName = null,
        IReadOnlyCollection<CreditNote>? creditNotes = null,
        // Defaulted, so the ~20 existing call sites are unchanged: an unattributed invoice and one whose caller did
        // not resolve names both render « non attribué », which is the honest reading of both.
        string? doctorName = null) => new()
    {
        Id = invoice.Id,
        PatientId = invoice.PatientId,
        PatientName = patientName,
        DentalRecordId = invoice.DentalRecordId,
        AppointmentId = invoice.AppointmentId,
        DoctorId = invoice.DoctorId,
        DoctorName = doctorName,
        TreatmentPlanId = invoice.TreatmentPlanId,
        Number = invoice.Number,
        IssueDate = invoice.IssueDate,
        Status = invoice.Status.ToString(),
        VatApplicable = invoice.VatApplicable,
        VatRate = invoice.VatRate,
        StampDutyAmount = invoice.StampDutyAmount,
        CancellationReason = invoice.CancellationReason,
        TotalHt = invoice.TotalHt,
        TotalVat = invoice.TotalVat,
        TotalTtc = invoice.TotalTtc,
        AmountCollected = invoice.AmountCollected,
        Outstanding = invoice.Outstanding,
        CreatedAt = invoice.CreatedAt,
        Version = invoice.Version,
        UpdatedAt = invoice.UpdatedAt,
        Lines = invoice.Lines
            .Select(l => new InvoiceLineDto
            {
                Id = l.Id,
                Designation = l.Designation,
                Quantity = l.Quantity,
                UnitPriceHt = l.UnitPriceHt,
                LineTotalHt = l.LineTotalHt,
                DentalRecordId = l.DentalRecordId,
                DentalActCodeId = l.DentalActCodeId,
                CodeActe = l.CodeActe
            })
            .ToList(),
        CanCancel = invoice.CanCancel,
        CanCreateAvoir = invoice.CanCreateCreditNote,
        CreditedTotal = creditNotes?.Sum(c => c.Amount) ?? 0m,
        CreditNotes = creditNotes?.Select(c => c.ToDto(invoice)).ToList() ?? new List<CreditNoteDto>(),
        // CreatedAt is the tiebreaker: two payments on the same day are common, and the detail modal needs a
        // deterministic order to diff against.
        Payments = invoice.Payments
            .OrderBy(p => p.PaidOn)
            .ThenBy(p => p.CreatedAt)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                Amount = p.Amount,
                Method = p.Method.ToString(),
                PaidOn = p.PaidOn,
                CreatedAt = p.CreatedAt,
                IsVoided = p.IsVoided,
                VoidedAt = p.VoidedAt,
                VoidReason = p.VoidReason,
                VoidedByName = p.VoidedByName,
                SourceInstallmentPaymentId = p.SourceInstallmentPaymentId
            })
            .ToList()
    };

    /// <summary>
    /// Maps an avoir to its DTO.
    /// </summary>
    public static CreditNoteDto ToDto(this CreditNote creditNote, Invoice? correctedInvoice = null) => new()
    {
        Id = creditNote.Id,
        InvoiceId = creditNote.InvoiceId,
        Number = creditNote.Number,
        IssueDate = creditNote.IssueDate,
        Amount = creditNote.Amount,
        Reason = creditNote.Reason,
        Method = creditNote.Method?.ToString(),
        RefundedOn = creditNote.RefundedOn
    };
}
