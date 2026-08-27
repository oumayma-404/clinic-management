namespace ClinicManagement.Application.Common.Models;

/// <summary>
/// Rendering model for a patient payment receipt (reçu) PDF — issued when an invoice payment or a
/// treatment-plan installment payment is recorded. Amounts in TND millimes. Not a fiscal document.
/// </summary>
public class ReceiptPdfData
{
    // Clinic identity
    public string ClinicName { get; set; } = string.Empty;
    public string? ClinicAddress { get; set; }
    public string? ClinicPhone { get; set; }
    public string? MatriculeFiscal { get; set; }

    public string PatientName { get; set; } = string.Empty;

    /// <summary>When the payment was received.</summary>
    public DateTime PaidOn { get; set; }

    /// <summary>Amount received.</summary>
    public decimal Amount { get; set; }

    /// <summary>Payment method, French label (Espèces / Chèque / Carte / Virement).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>What the payment settled (e.g. "Note d'honoraires N° 2026-0004" or "Devis … — échéance du …").</summary>
    public string For { get; set; } = string.Empty;

    /// <summary>
    /// Remaining balance on the settled document <b>as of this payment</b> (reste à payer) — not the live
    /// balance. A receipt states what was true when it was issued; printing the current figure made a reprint
    /// of the first of two receipts show a balance that never applied, and after a void it would show one
    /// that had grown.
    /// </summary>
    public decimal RemainingBalance { get; set; }

    /// <summary>Optional reference for the receipt (e.g. the source document number).</summary>
    public string? Reference { get; set; }

    /// <summary>
    /// True when the payment has since been voided. The receipt still renders — the paper is already in the
    /// patient's hands and the clinic needs to reproduce what was handed over — but it is over-stamped, so a
    /// reversed payment can never be reprinted as a clean one.
    /// </summary>
    public bool IsVoided { get; set; }
    public DateTime? VoidedOn { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>Generated receipt PDF plus a suggested download file name.</summary>
public class ReceiptPdfResult
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = "recu.pdf";
}
