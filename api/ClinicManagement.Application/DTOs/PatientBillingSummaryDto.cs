namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The unified per-patient money view (« Solde patient ») — the single balance across both billing tracks
/// (issued invoices + treatment-plan installments) plus the indicative CNAM split over everything billed.
/// All amounts in TND. Computed on read; never persisted.
/// </summary>
public class PatientBillingSummaryDto
{
    /// <summary>Outstanding across the patient's issued, non-cancelled invoices (Σ TTC − collected).</summary>
    public decimal InvoiceOutstanding { get; set; }

    /// <summary>Outstanding across the patient's non-cancelled treatment-plan installments (Σ amount − paid).</summary>
    public decimal InstallmentOutstanding { get; set; }

    /// <summary>The single « Solde patient » = invoice outstanding + installment outstanding.</summary>
    public decimal TotalOutstanding { get; set; }

    /// <summary>Oldest overdue installment due date (unpaid, past due), or null if nothing is overdue.</summary>
    public DateTime? OldestOverdueDate { get; set; }

    /// <summary>Indicative CNAM-reimbursable portion across everything billed to the patient.</summary>
    public decimal CnamReimbursable { get; set; }

    /// <summary>Patient out-of-pocket (reste à charge) = total billed − CNAM-reimbursable.</summary>
    public decimal PatientOutOfPocket { get; set; }
}
