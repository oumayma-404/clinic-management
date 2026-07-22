using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Invoices;

/// <summary>Maps <see cref="Invoice"/> aggregates to their DTOs.</summary>
public static class InvoiceMappingExtensions
{
    public static InvoiceDto ToDto(this Invoice invoice, string? patientName = null) => new()
    {
        Id = invoice.Id,
        PatientId = invoice.PatientId,
        PatientName = patientName,
        DentalRecordId = invoice.DentalRecordId,
        AppointmentId = invoice.AppointmentId,
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
        UpdatedAt = invoice.UpdatedAt,
        EInvoiceStatus = invoice.EInvoiceStatus.ToString(),
        TtnIdentifier = invoice.TtnIdentifier,
        EInvoiceSubmittedAt = invoice.EInvoiceSubmittedAt,
        EInvoiceValidatedAt = invoice.EInvoiceValidatedAt,
        EInvoiceLastError = invoice.EInvoiceLastError,
        EInvoiceAttemptCount = invoice.EInvoiceAttemptCount,
        CanSubmitToElFatoora = invoice.CanSubmitToElFatoora,
        HasSignedXml = !string.IsNullOrWhiteSpace(invoice.SignedXmlStorageKey),
        HasTtnReceipt = !string.IsNullOrWhiteSpace(invoice.TtnReceiptStorageKey),
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
        Payments = invoice.Payments
            .OrderBy(p => p.PaidOn)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                Amount = p.Amount,
                Method = p.Method.ToString(),
                PaidOn = p.PaidOn
            })
            .ToList()
    };
}
