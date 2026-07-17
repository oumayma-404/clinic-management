namespace ClinicManagement.Application.Common.Models;

/// <summary>
/// Everything the TEIF XML generator needs from an issued invoice + its clinic (seller) and patient
/// (buyer). A flat, infrastructure-agnostic snapshot so TEIF generation stays a pure transform (FR-1).
/// Amounts are the frozen invoice totals (TND millimes).
/// </summary>
public class TeifInvoiceInput
{
    // Document header
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    /// <summary>TEIF document type code — 380 = commercial invoice (the only type in scope).</summary>
    public string DocumentTypeCode { get; set; } = "380";
    public string CurrencyCode { get; set; } = "TND";

    // Seller (clinic)
    public string SellerName { get; set; } = string.Empty;
    public string? SellerAddress { get; set; }
    public string? SellerMatriculeFiscal { get; set; }

    // Buyer (patient) — B2C final consumer by default (FR-6); MF present for B2B.
    public string BuyerName { get; set; } = string.Empty;
    public string? BuyerNationalId { get; set; }
    public string? BuyerMatriculeFiscal { get; set; }

    // Tax posture (frozen at issue)
    public bool VatApplicable { get; set; }
    public decimal VatRate { get; set; }

    // Monetary totals
    public decimal TotalHt { get; set; }
    public decimal TotalVat { get; set; }
    public decimal StampDutyAmount { get; set; }
    public decimal TotalTtc { get; set; }

    public List<TeifInvoiceLineInput> Lines { get; set; } = new();
}

public class TeifInvoiceLineInput
{
    public string Designation { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceHt { get; set; }
    public decimal LineTotalHt { get; set; }
}

/// <summary>The signed TEIF XML (enveloped XAdES/XMLDSig), ready to submit to TTN.</summary>
public class SignedEInvoiceResult
{
    public string SignedXml { get; set; } = string.Empty;
}

/// <summary>Outcome of a TTN « El Fatoora » submission attempt.</summary>
public enum TtnSubmissionOutcome
{
    /// <summary>Accepted + validated by TTN — a unique identifier (and receipt) was returned.</summary>
    Validated = 0,
    /// <summary>Permanently rejected (bad data/schema) — needs correction, not a retry.</summary>
    Rejected = 1,
    /// <summary>Transient failure (network / TTN outage) — safe to retry via the outbox.</summary>
    TransientFailure = 2
}

/// <summary>Result returned by an <c>ITtnClient</c> submission.</summary>
public class TtnSubmissionResult
{
    public TtnSubmissionOutcome Outcome { get; set; }
    public string? TtnIdentifier { get; set; }
    /// <summary>Raw receipt/acknowledgement content (XML/JSON) to persist as the legal record.</summary>
    public string? ReceiptContent { get; set; }
    public string? Error { get; set; }

    public static TtnSubmissionResult Validated(string ttnIdentifier, string? receipt) =>
        new() { Outcome = TtnSubmissionOutcome.Validated, TtnIdentifier = ttnIdentifier, ReceiptContent = receipt };

    public static TtnSubmissionResult Rejected(string error, string? receipt = null) =>
        new() { Outcome = TtnSubmissionOutcome.Rejected, Error = error, ReceiptContent = receipt };

    public static TtnSubmissionResult Transient(string error) =>
        new() { Outcome = TtnSubmissionOutcome.TransientFailure, Error = error };
}

/// <summary>A downloadable e-invoicing legal artifact (signed TEIF XML or TTN receipt).</summary>
public class EInvoiceArtifactResult
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = "artifact";
    public string ContentType { get; set; } = "application/octet-stream";
}
