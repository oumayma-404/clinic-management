namespace ClinicManagement.Application.Common.Models;

/// <summary>
/// Rendering model for an avoir (credit note) PDF — the document the clinic hands a patient when it credits
/// back part or all of a settled note d'honoraires.
///
/// <para>
/// Most of this cannot come from <c>CreditNote</c> alone, which is why the model exists. A legal avoir must
/// cite <b>the invoice it corrects, by number and date</b>, and the entity holds only a soft <c>InvoiceId</c>
/// with no navigation. It also carries a single scalar <c>Amount</c>, so the HT/TVA split has to be derived
/// from the corrected invoice's frozen VAT posture — a VAT-applicable clinic's avoir cannot legitimately show
/// one undifferentiated figure.
/// </para>
/// <para>
/// Two dates deliberately appear: <see cref="IssueDate"/> (when the document was drawn up, always the moment
/// of creation) and <see cref="RefundedOn"/> (when the money actually went back, caller-supplied and the date
/// the caisse nets against). They are frequently different and an accountant needs both.
/// </para>
/// </summary>
public class AvoirPdfData
{
    // Clinic identity — an avoir is a fiscal correction document, so it carries the same header as the invoice.
    public string ClinicName { get; set; } = string.Empty;
    public string? ClinicAddress { get; set; }
    public string? ClinicPhone { get; set; }
    public string? MatriculeFiscal { get; set; }

    public string PatientName { get; set; } = string.Empty;

    /// <summary>The avoir's own number, from its own per-clinic-per-year sequence.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>When the avoir was drawn up.</summary>
    public DateTime IssueDate { get; set; }

    /// <summary>When the money was actually returned — the date the caisse nets against.</summary>
    public DateTime RefundedOn { get; set; }

    /// <summary>The corrected invoice's number. Mandatory on the document: an avoir must cite what it corrects.</summary>
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceIssueDate { get; set; }

    public decimal AmountHt { get; set; }
    public decimal AmountVat { get; set; }

    /// <summary>
    /// The share of the credited total that is <b>timbre fiscal</b>, not VAT base.
    ///
    /// <para>
    /// It has to be its own field because the timbre sits <b>outside</b> the VAT base on the corrected note
    /// (<c>ttc = ht + vat + stamp</c>). De-VATing the whole credited TTC therefore over-reports the TVA being
    /// reversed — on a 100 DT HT / 7 % / 1 DT note a full-value avoir declared HT 100,935 + TVA 7,065 instead of
    /// HT 100,000 + TVA 7,000 + timbre 1,000. An avoir reverses the tax that was actually charged, so the three
    /// figures must be the invoice's own frozen ones.
    /// </para>
    /// </summary>
    public decimal AmountStamp { get; set; }

    /// <summary>The credited total — the figure the patient sees, and the one stored on the entity.</summary>
    public decimal AmountTtc { get; set; }

    public bool VatApplicable { get; set; }
    public decimal VatRate { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>How the money went back (French label), when recorded.</summary>
    public string? Method { get; set; }

    /// <summary>
    /// True when the corrected invoice is registered with TTN. The avoir itself is NOT transmitted, so the
    /// document says so rather than letting the clinic assume it was declared.
    /// </summary>
    public bool CorrectedInvoiceIsTtnRegistered { get; set; }
}

/// <summary>Generated avoir PDF plus a suggested download file name.</summary>
public class AvoirPdfResult
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = "avoir.pdf";
}
