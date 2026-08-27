namespace ClinicManagement.Application.Common.Models;

/// <summary>
/// Rendering model for a dental devis (quote) PDF — a non-fiscal estimate (no VAT, no timbre).
/// Amounts in TND millimes.
/// </summary>
public class DevisPdfData
{
    // Clinic identity
    public string ClinicName { get; set; } = string.Empty;
    public string? ClinicAddress { get; set; }
    public string? ClinicPhone { get; set; }
    public string? MatriculeFiscal { get; set; }

    // Patient + document header
    public string PatientName { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;

    public decimal TotalPlanned { get; set; }

    // Payment state, from the plan's installment collections (montant réglé / reste à payer).
    public decimal AmountPaid { get; set; }
    public decimal Outstanding { get; set; }

    // Indicative CNAM split (never a fiscal figure): reimbursable + out-of-pocket == total planned.
    public decimal CnamReimbursable { get; set; }
    public decimal PatientOutOfPocket { get; set; }

    public List<DevisPdfLine> Lines { get; set; } = new();
    public List<DevisPdfInstallment> Installments { get; set; } = new();
}

public class DevisPdfLine
{
    public string? CodeActe { get; set; }
    public string Designation { get; set; } = string.Empty;
    public string Teeth { get; set; } = string.Empty;
    public decimal PlannedCost { get; set; }
}

public class DevisPdfInstallment
{
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Generated devis PDF plus a suggested download file name.</summary>
public class DevisPdfResult
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = "devis.pdf";
}
