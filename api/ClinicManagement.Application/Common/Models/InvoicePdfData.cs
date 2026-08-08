namespace ClinicManagement.Application.Common.Models;

/// <summary>Rendering model for a Tunisian "note d'honoraires" PDF (amounts in TND, no €/$, no "Paris").</summary>
public class InvoicePdfData
{
    // Clinic identity
    public string ClinicName { get; set; } = string.Empty;
    public string? ClinicAddress { get; set; }
    public string? ClinicPhone { get; set; }
    public string? MatriculeFiscal { get; set; }

    // Patient + document header
    public string PatientName { get; set; } = string.Empty;

    /// <summary>
    /// The patient's address on one line, or null when they have none (J10).
    /// <para>
    /// ⚠️ Deliberately <b>optional, and never a validation blocker</b>. Code TVA art. 18 § II requires the
    /// client's address only for clients subject to the déclaration d'existence — businesses, not a private
    /// patient — so a note d'honoraires for a walk-in is complete without it. It is here because it is useful
    /// (a printed note is what a patient files, forwards to an insurer, or attaches to a CNAM claim), and the
    /// render model previously had no patient address at all. A patient with none simply renders one line fewer.
    /// </para>
    /// </summary>
    public string? PatientAddress { get; set; }

    public string Number { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }

    // Frozen amounts (TND millimes)
    public bool VatApplicable { get; set; }
    public decimal VatRate { get; set; }
    public decimal TotalHt { get; set; }
    public decimal TotalVat { get; set; }
    public decimal StampDutyAmount { get; set; }
    public decimal TotalTtc { get; set; }

    // Payment state (montant réglé / reste à payer), from the invoice's recorded payments.
    public decimal AmountCollected { get; set; }
    public decimal Outstanding { get; set; }

    // Indicative CNAM split (never a fiscal figure): reimbursable + out-of-pocket == TTC.
    public decimal CnamReimbursable { get; set; }
    public decimal PatientOutOfPocket { get; set; }

    public bool IsCancelled { get; set; }


    public List<InvoicePdfLine> Lines { get; set; } = new();
}

public class InvoicePdfLine
{
    public string Designation { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceHt { get; set; }
    public decimal LineTotalHt { get; set; }
}

/// <summary>Generated invoice PDF plus a suggested download file name.</summary>
public class InvoicePdfResult
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = "note-honoraires.pdf";
}
