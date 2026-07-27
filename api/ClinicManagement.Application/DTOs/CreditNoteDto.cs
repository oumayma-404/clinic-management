namespace ClinicManagement.Application.DTOs;

/// <summary>An avoir (credit note) issued against an invoice.</summary>
public class CreditNoteDto
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Method { get; set; }
    public DateTime RefundedOn { get; set; }

    /// <summary>
    /// True when the corrected invoice is registered with TTN « El Fatoora ». The avoir is NOT transmitted to
    /// TTN, so both the screen and the PDF have to say so rather than let the clinic assume it was declared.
    /// </summary>
    public bool CorrectedInvoiceIsTtnRegistered { get; set; }
}
