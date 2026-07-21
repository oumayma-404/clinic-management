namespace ClinicManagement.Application.DTOs;

public class InvoiceDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public Guid? DentalRecordId { get; set; }
    public Guid? AppointmentId { get; set; }
    public string? Number { get; set; }
    public DateTime? IssueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool VatApplicable { get; set; }
    public decimal VatRate { get; set; }
    public decimal StampDutyAmount { get; set; }
    public string? CancellationReason { get; set; }
    public decimal TotalHt { get; set; }
    public decimal TotalVat { get; set; }
    public decimal TotalTtc { get; set; }
    public decimal AmountCollected { get; set; }
    public decimal Outstanding { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // TTN « El Fatoora » electronic-invoicing state (FR-5). Secrets/blobs are never exposed — only status,
    // the public TTN reference, timestamps, the last error, and download-availability flags.
    public string EInvoiceStatus { get; set; } = string.Empty;
    public string? TtnIdentifier { get; set; }
    public DateTime? EInvoiceSubmittedAt { get; set; }
    public DateTime? EInvoiceValidatedAt { get; set; }
    public string? EInvoiceLastError { get; set; }
    public int EInvoiceAttemptCount { get; set; }
    public bool CanSubmitToElFatoora { get; set; }
    public bool HasSignedXml { get; set; }
    public bool HasTtnReceipt { get; set; }

    public List<InvoiceLineDto> Lines { get; set; } = new();
    public List<PaymentDto> Payments { get; set; } = new();
}

public class InvoiceLineDto
{
    public Guid Id { get; set; }
    public string Designation { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceHt { get; set; }
    public decimal LineTotalHt { get; set; }
    public Guid? DentalRecordId { get; set; }
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime PaidOn { get; set; }
}

/// <summary>Aggregate revenue for the Recettes view: invoiced / collected / outstanding (TND).</summary>
public class InvoiceRevenueDto
{
    public decimal TotalInvoiced { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal Outstanding { get; set; }
}

/// <summary>One requested act line when creating/updating an invoice.</summary>
public class InvoiceLineRequest
{
    public string Designation { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceHt { get; set; }

    /// <summary>Optional dental record this line bills (drives the "already invoiced" guard, FR-1.2).</summary>
    public Guid? DentalRecordId { get; set; }
}
