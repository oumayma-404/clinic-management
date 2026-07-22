namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One row of the clinic-wide « Créances » (accounts-receivable) list: a patient who owes money, their
/// total outstanding across both billing tracks, and the aging of their oldest overdue installment.
/// </summary>
public class ReceivableDto
{
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;

    /// <summary>Total owed across issued invoices + treatment-plan installments (TND).</summary>
    public decimal TotalOutstanding { get; set; }

    /// <summary>Oldest overdue installment due date, or null if nothing is overdue.</summary>
    public DateTime? OldestOverdueDate { get; set; }

    /// <summary>Whole days since the oldest overdue installment fell due, or null if nothing is overdue.</summary>
    public int? DaysOverdue { get; set; }
}
