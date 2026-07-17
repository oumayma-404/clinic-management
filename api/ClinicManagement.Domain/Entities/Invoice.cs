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

    /// <summary>Sequential number <c>AAAA-NNNN</c>; null while a draft (assigned at issue).</summary>
    public string? Number { get; private set; }
    public DateTime? IssueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }

    // VAT / stamp — frozen at issue from the clinic settings.
    public bool VatApplicable { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal StampDutyAmount { get; private set; }

    public string? CancellationReason { get; private set; }

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

    private Invoice() { } // For EF Core

    public Invoice(
        Guid id,
        Guid clinicId,
        Guid patientId,
        Guid? dentalRecordId = null,
        Guid? appointmentId = null)
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
        Status = InvoiceStatus.Draft;
        CreatedAt = DateTime.UtcNow;
        RecomputeTotals();
    }

    /// <summary>Replace all act lines. Draft only.</summary>
    public void SetLines(IEnumerable<(string designation, int quantity, decimal unitPriceHt)> lines)
    {
        EnsureDraft();
        _lines.Clear();
        foreach (var (designation, quantity, unitPriceHt) in lines)
        {
            _lines.Add(new InvoiceLine(Guid.NewGuid(), Id, designation, quantity, unitPriceHt));
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

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif d'annulation est requis.", nameof(reason));

        CancellationReason = reason.Trim();
        Status = InvoiceStatus.Cancelled;
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
