using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// The fiscal document of record: a Tunisian "note d'honoraires" (aggregate root, clinic-scoped).
/// A draft (no number, editable, deletable) is issued to receive a per-clinic sequential number
/// (<c>AAAA-NNNN</c>) with VAT/stamp settings and totals frozen at emission; payments then move it to
/// PartiallyPaid/Paid, or it can be cancelled (number kept, no further payments).
/// </summary>
public class Invoice : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid? DentalRecordId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    /// <summary>The treatment plan (devis) this note was generated from, if any — the devis→facture link
    /// used by « Solde patient » to count the invoice instead of the plan (no double-count).</summary>
    public Guid? TreatmentPlanId { get; private set; }

    /// <summary>Sequential number <c>AAAA-NNNN</c>; null while a draft (assigned at issue).</summary>
    public string? Number { get; private set; }
    public DateTime? IssueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }

    // VAT / stamp — frozen at issue from the clinic settings.
    public bool VatApplicable { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal StampDutyAmount { get; private set; }

    public string? CancellationReason { get; private set; }

    // --- TTN « El Fatoora » electronic-invoicing state (FR-5). Independent of the fiscal Status above.
    public EInvoiceStatus EInvoiceStatus { get; private set; }
    /// <summary>The unique identifier TTN returns once the e-invoice is accepted/validated.</summary>
    public string? TtnIdentifier { get; private set; }
    /// <summary>File-storage key of the signed TEIF XML (a legal record — stored immutably).</summary>
    public string? SignedXmlStorageKey { get; private set; }
    /// <summary>File-storage key of the TTN receipt/acknowledgement.</summary>
    public string? TtnReceiptStorageKey { get; private set; }
    /// <summary>Payload encoded into the QR « cachet électronique visible » once validated.</summary>
    public string? QrPayload { get; private set; }
    public DateTime? EInvoiceSubmittedAt { get; private set; }
    public DateTime? EInvoiceValidatedAt { get; private set; }
    public string? EInvoiceLastError { get; private set; }
    /// <summary>Number of dispatch attempts made by the outbox (drives bounded retry).</summary>
    public int EInvoiceAttemptCount { get; private set; }
    /// <summary>Earliest time the outbox may (re)attempt dispatch — implements backoff.</summary>
    public DateTime? EInvoiceNextAttemptAt { get; private set; }

    // Totals (TND millimes) — recomputed from lines + frozen VAT/stamp.
    public decimal TotalHt { get; private set; }
    public decimal TotalVat { get; private set; }
    public decimal TotalTtc { get; private set; }
    public decimal AmountCollected { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<InvoiceLine> _lines = new();
    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    private readonly List<Payment> _payments = new();
    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    /// <summary>Outstanding balance = TTC − collected (never negative).</summary>
    public decimal Outstanding => Math.Max(0m, TotalTtc - AmountCollected);

    /// <summary>Only a draft can be deleted; an issued invoice is cancelled instead.</summary>
    public bool CanBeDeleted => Status == InvoiceStatus.Draft;

    /// <summary>
    /// True when the invoice may be sent (or re-sent) to El Fatoora: it is fiscally issued (numbered, not
    /// a draft, not cancelled) and not already validated or mid-flight at TTN. Idempotency guard for FR-4.
    /// </summary>
    public bool CanSubmitToElFatoora =>
        (Status == InvoiceStatus.Issued || Status == InvoiceStatus.PartiallyPaid || Status == InvoiceStatus.Paid)
        && EInvoiceStatus != EInvoiceStatus.Valid
        && EInvoiceStatus != EInvoiceStatus.Submitted
        && EInvoiceStatus != EInvoiceStatus.Validating;

    private Invoice() { } // For EF Core

    public Invoice(
        Guid id,
        Guid clinicId,
        Guid patientId,
        Guid? dentalRecordId = null,
        Guid? appointmentId = null,
        Guid? treatmentPlanId = null)
    {
        if (clinicId == Guid.Empty)
            throw new ArgumentException("Le cabinet est requis.", nameof(clinicId));

        if (patientId == Guid.Empty)
            throw new ArgumentException("Le patient est requis.", nameof(patientId));

        Id = id;
        ClinicId = clinicId;
        PatientId = patientId;
        DentalRecordId = dentalRecordId;
        AppointmentId = appointmentId;
        TreatmentPlanId = treatmentPlanId;
        Status = InvoiceStatus.Draft;
        CreatedAt = DateTime.UtcNow;
        RecomputeTotals();
    }

    /// <summary>Replace all act lines. Draft only.</summary>
    public void SetLines(IEnumerable<(string designation, int quantity, decimal unitPriceHt)> lines)
        => SetLines(lines.Select(l => (l.designation, l.quantity, l.unitPriceHt, (Guid?)null, (Guid?)null, (string?)null)));

    /// <summary>
    /// Replace all act lines, each optionally linked to the dental record it bills (so a multi-record note
    /// d'honoraires marks every seeded record invoiced, not only the single header link). Draft only.
    /// </summary>
    public void SetLines(IEnumerable<(string designation, int quantity, decimal unitPriceHt, Guid? dentalRecordId)> lines)
        => SetLines(lines.Select(l => (l.designation, l.quantity, l.unitPriceHt, l.dentalRecordId, (Guid?)null, (string?)null)));

    /// <summary>
    /// Replace all act lines, each optionally linked to the dental record it bills and to the catalog CNAM/DCH
    /// act it charges (the act link drives the indicative CNAM-reimbursable vs. out-of-pocket split). Draft only.
    /// </summary>
    public void SetLines(IEnumerable<(string designation, int quantity, decimal unitPriceHt, Guid? dentalRecordId, Guid? dentalActCodeId, string? codeActe)> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var (designation, quantity, unitPriceHt, dentalRecordId, dentalActCodeId, codeActe) in lines)
        {
            _lines.Add(new InvoiceLine(Guid.NewGuid(), Id, designation, quantity, unitPriceHt, dentalRecordId, dentalActCodeId, codeActe));
        }
        RecomputeTotals();
        Touch();
    }

    /// <summary>Repoint the draft to a (possibly different) patient / source links. Draft only.</summary>
    public void UpdateLinks(Guid patientId, Guid? dentalRecordId, Guid? appointmentId)
    {
        EnsureDraft();
        if (patientId == Guid.Empty)
            throw new ArgumentException("Le patient est requis.", nameof(patientId));

        PatientId = patientId;
        DentalRecordId = dentalRecordId;
        AppointmentId = appointmentId;
        Touch();
    }

    /// <summary>
    /// Emit the draft: assign its (externally computed, unique) sequential number, freeze the VAT/stamp
    /// settings from the clinic, recompute the totals, and move it to Issued. Requires at least one line.
    /// </summary>
    public void Issue(string number, bool vatApplicable, decimal vatRate, bool stampDutyEnabled, decimal stampDutyAmount)
    {
        EnsureDraft();

        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Le numéro de facture est requis.", nameof(number));

        if (_lines.Count == 0)
            throw new InvalidOperationException("Une facture doit comporter au moins une ligne pour être émise.");

        Number = number.Trim();
        IssueDate = DateTime.UtcNow;
        VatApplicable = vatApplicable;
        VatRate = vatApplicable ? vatRate : 0m;
        StampDutyAmount = stampDutyEnabled ? stampDutyAmount : 0m;
        Status = InvoiceStatus.Issued;
        RecomputeTotals();
        Touch();
    }

    /// <summary>
    /// Reassign the sequential number on an already-issued invoice. Used only to resolve a concurrent
    /// numbering collision during issuance (unique-constraint retry) — not a general-purpose mutator.
    /// </summary>
    public void SetIssuedNumber(string number)
    {
        if (Status == InvoiceStatus.Draft)
            throw new InvalidOperationException("Le numéro ne peut être réattribué qu'à une facture émise.");

        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Le numéro de facture est requis.", nameof(number));

        Number = number.Trim();
        Touch();
    }

    /// <summary>
    /// Record a payment. Allowed only on an issued/partially-paid invoice. An overpayment (collected
    /// beyond the TTC) is refused; reaching the TTC exactly moves the invoice to Paid.
    /// </summary>
    public void RecordPayment(decimal amount, PaymentMethod method, DateTime paidOn)
    {
        if (Status != InvoiceStatus.Issued && Status != InvoiceStatus.PartiallyPaid)
            throw new InvalidOperationException("Un paiement ne peut être enregistré que sur une facture émise.");

        if (amount <= 0)
            throw new ArgumentException("Le montant du paiement doit être supérieur à 0.", nameof(amount));

        if (AmountCollected + amount > TotalTtc)
            throw new InvalidOperationException("Le paiement dépasse le montant restant dû.");

        _payments.Add(new Payment(Guid.NewGuid(), Id, amount, method, paidOn));
        AmountCollected += amount;
        Status = AmountCollected >= TotalTtc ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        Touch();
    }

    /// <summary>
    /// Cancel an issued invoice (motif required). The number, lines and frozen totals are kept; no
    /// further payment is possible. A draft is deleted, not cancelled.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == InvoiceStatus.Draft)
            throw new InvalidOperationException("Un brouillon se supprime, il ne s'annule pas.");

        if (Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("La facture est déjà annulée.");

        // A note with recorded payments must not be silently voided — that would erase collected cash from
        // the caisse with no trail. Corrections go through an avoir (credit note).
        if (_payments.Count > 0)
            throw new InvalidOperationException("Une facture avec des paiements enregistrés ne peut pas être annulée. Établissez un avoir.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif d'annulation est requis.", nameof(reason));

        CancellationReason = reason.Trim();
        Status = InvoiceStatus.Cancelled;
        Touch();
    }

    /// <summary>
    /// Queue the invoice for El Fatoora submission (the offline outbox entry point, US-1/US-2). Allowed only
    /// on a fiscally-issued, non-validated invoice; resets the retry budget and makes it due immediately.
    /// </summary>
    public void QueueForElFatoora()
    {
        if (!CanSubmitToElFatoora)
            throw new InvalidOperationException("Cette facture ne peut pas être envoyée à El Fatoora dans son état actuel.");

        EInvoiceStatus = EInvoiceStatus.Queued;
        EInvoiceLastError = null;
        EInvoiceAttemptCount = 0;
        EInvoiceNextAttemptAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>Record that the signed TEIF has been produced and stored (transient state before submission).</summary>
    public void MarkEInvoiceSigned(string signedXmlStorageKey)
    {
        if (string.IsNullOrWhiteSpace(signedXmlStorageKey))
            throw new ArgumentException("La clé du XML signé est requise.", nameof(signedXmlStorageKey));

        SignedXmlStorageKey = signedXmlStorageKey.Trim();
        EInvoiceStatus = EInvoiceStatus.Signed;
        Touch();
    }

    /// <summary>Record that the signed e-invoice was accepted by TTN and is awaiting final validation.</summary>
    public void MarkEInvoiceSubmitted(string ttnIdentifier, string? receiptStorageKey)
    {
        if (string.IsNullOrWhiteSpace(ttnIdentifier))
            throw new ArgumentException("L'identifiant TTN est requis.", nameof(ttnIdentifier));

        TtnIdentifier = ttnIdentifier.Trim();
        TtnReceiptStorageKey = string.IsNullOrWhiteSpace(receiptStorageKey) ? TtnReceiptStorageKey : receiptStorageKey.Trim();
        EInvoiceStatus = EInvoiceStatus.Submitted;
        EInvoiceSubmittedAt = DateTime.UtcNow;
        EInvoiceLastError = null;
        EInvoiceNextAttemptAt = null;
        Touch();
    }

    /// <summary>Record TTN validation: the invoice is now a legally-registered e-invoice with a QR cachet.</summary>
    public void MarkEInvoiceValidated(string ttnIdentifier, string qrPayload, string? receiptStorageKey)
    {
        if (string.IsNullOrWhiteSpace(ttnIdentifier))
            throw new ArgumentException("L'identifiant TTN est requis.", nameof(ttnIdentifier));

        if (string.IsNullOrWhiteSpace(qrPayload))
            throw new ArgumentException("Le contenu du QR cachet est requis.", nameof(qrPayload));

        TtnIdentifier = ttnIdentifier.Trim();
        QrPayload = qrPayload;
        TtnReceiptStorageKey = string.IsNullOrWhiteSpace(receiptStorageKey) ? TtnReceiptStorageKey : receiptStorageKey.Trim();
        EInvoiceStatus = EInvoiceStatus.Valid;
        EInvoiceValidatedAt = DateTime.UtcNow;
        EInvoiceSubmittedAt ??= DateTime.UtcNow;
        EInvoiceLastError = null;
        EInvoiceNextAttemptAt = null;
        Touch();
    }

    /// <summary>
    /// Record a permanent TTN rejection (bad data/schema). Requires correction + resubmission. An optional
    /// rejection receipt/ack (its file-storage key) is kept so the operator can inspect the TTN reason.
    /// </summary>
    public void MarkEInvoiceRejected(string reason, string? receiptStorageKey = null)
    {
        EInvoiceStatus = EInvoiceStatus.Rejected;
        EInvoiceLastError = string.IsNullOrWhiteSpace(reason) ? "Rejetée par El Fatoora." : reason.Trim();
        if (!string.IsNullOrWhiteSpace(receiptStorageKey))
        {
            TtnReceiptStorageKey = receiptStorageKey.Trim();
        }
        EInvoiceNextAttemptAt = null;
        Touch();
    }

    /// <summary>
    /// Record a transient dispatch failure. Consumes one attempt; stays <c>Queued</c> (retried after
    /// <paramref name="nextAttemptAt"/>) until <paramref name="maxAttempts"/> is reached, then → <c>Failed</c>.
    /// </summary>
    public void RecordEInvoiceFailure(string error, int maxAttempts, DateTime nextAttemptAt)
    {
        EInvoiceAttemptCount++;
        EInvoiceLastError = string.IsNullOrWhiteSpace(error) ? "Échec de l'envoi à El Fatoora." : error.Trim();

        if (EInvoiceAttemptCount >= maxAttempts)
        {
            EInvoiceStatus = EInvoiceStatus.Failed;
            EInvoiceNextAttemptAt = null;
        }
        else
        {
            EInvoiceStatus = EInvoiceStatus.Queued;
            EInvoiceNextAttemptAt = nextAttemptAt;
        }
        Touch();
    }

    private void RecomputeTotals()
    {
        var ht = _lines.Sum(l => l.LineTotalHt);
        var totals = InvoiceCalculator.Compute(ht, VatApplicable, VatRate, StampDutyAmount);
        TotalHt = totals.TotalHt;
        TotalVat = totals.TotalVat;
        TotalTtc = totals.TotalTtc;
    }

    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Seule une facture au statut brouillon peut être modifiée.");
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
