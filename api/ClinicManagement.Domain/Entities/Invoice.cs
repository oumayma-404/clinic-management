using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;

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
    /// <summary>
    /// Which practitioner earned this — nullable, and nullable means nullable (L9 attribution).
    ///
    /// <para><b>What was missing.</b> <c>DoctorId</c> existed on exactly three entities in the whole model
    /// (<c>Appointment</c> — the only real FK to <c>Doctors</c> — <c>RecurringAppointment</c>, and
    /// <c>WaitingListEntry.PreferredDoctorId</c>, which was not even an FK), and on nothing that carries money or
    /// clinical work. So « combien a produit ce praticien ce mois ? » had no answer, and
    /// <c>Features/Dashboard/</c> contained <b>zero</b> occurrences of <c>Doctor</c> across all four readers.</para>
    ///
    /// <para>⚠️ <b>Historical rows legitimately have none</b> — the column did not exist when they were written,
    /// and the migration only backfills where a linked appointment names a practitioner. Every read must therefore
    /// tolerate null rather than treating it as « the clinic », which would silently attribute one dentist's work
    /// to whoever the filter happens to select.</para>
    ///
    /// <para>This is <b>attribution, not authorization</b>: it answers who earned a figure. Per-practitioner data
    /// scoping (« this dentist sees only their own patients ») is a separate decision with its own blast radius and
    /// is deliberately out of scope.</para>
    /// </summary>
    public Guid? DoctorId { get; private set; }

    /// <summary>The practitioner navigation, for the read-side name resolution. Null when unattributed.</summary>
    public Doctor? Doctor { get; private set; }

    /// <summary>
    /// Attribute (or un-attribute) this record to a practitioner. Deliberately its own mutator rather than a ctor
    /// parameter on every construction path: the answer is often only known *after* the aggregate exists (it comes
    /// from the appointment the record was written against), and a required ctor argument would have forced every
    /// caller to guess.
    /// </summary>
    public void SetDoctor(Guid? doctorId)
    {
        DoctorId = doctorId == Guid.Empty ? null : doctorId;
        Touch();
    }


    /// <summary>Sequential number <c>AAAA-NNNN</c>; null while a draft (assigned at issue).</summary>
    public string? Number { get; private set; }
    public DateTime? IssueDate { get; private set; }
    public InvoiceStatus Status { get; private set; }

    // ⚠️ HISTORICAL ONLY. The product no longer applies TVA or a timbre fiscal: an act's price is the whole of
    // what the patient owes. These three stay because an invoice issued before that change is a numbered legal
    // document that really did carry them, and it must keep rendering with the figures it was issued with.
    // Nothing writes them any more — `Issue` leaves them at their zero defaults — so on every new invoice they
    // are false/0/0. Read them to display history; never to compute a total.
    public bool VatApplicable { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal StampDutyAmount { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>
    /// The note that replaced this one after a correction — set on the <b>old</b> note when it is superseded.
    ///
    /// <para>A cancelled note is otherwise a dead end: the reader sees « annulée » and has no way to reach what
    /// replaced it, which is precisely the question anyone asks. Both directions are stored because both are
    /// asked: from the old note « what took its place », from the new one « what was this correcting ».</para>
    /// </summary>
    public Guid? SupersededByInvoiceId { get; private set; }

    /// <summary>The note this one corrects — set on the <b>replacement</b>. Null on an ordinary note.</summary>
    public Guid? SupersedesInvoiceId { get; private set; }

    /// <summary>
    /// Why the note being replaced was wrong. Captured when the correction starts and spent when the
    /// replacement is issued — that is the moment the predecessor's payments are voided and the note cancelled,
    /// and both of those refuse to happen without a reason.
    /// </summary>
    public string? SupersedesReason { get; private set; }

    // Totals (TND millimes) — recomputed from lines. `TotalVat` is 0 on every invoice issued since TVA was
    // dropped, and `TotalTtc == TotalHt`; on a historical row both keep the values frozen at its own issue.
    public decimal TotalHt { get; private set; }
    public decimal TotalVat { get; private set; }
    public decimal TotalTtc { get; private set; }
    public decimal AmountCollected { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<InvoiceLine> _lines = new();
    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// Detach every line raised from a fiche de soins that is being deleted, returning how many were cleared.
    /// <para>
    /// The invoice keeps its number, its lines, its amounts and its status — deleting a clinical record must never
    /// alter a fiscal document. Only the FK-less provenance pointer is dropped, so no line is left referencing a
    /// row that no longer exists. Works on a cancelled invoice too: a dangling pointer is just as wrong there.
    /// </para>
    /// </summary>
    public int ClearDentalRecordLinks(Guid dentalRecordId)
    {
        var affected = _lines.Where(l => l.DentalRecordId == dentalRecordId).ToList();
        foreach (var line in affected)
        {
            line.ClearDentalRecordLink();
        }

        if (affected.Count > 0)
        {
            UpdatedAt = DateTime.UtcNow;
        }

        return affected.Count;
    }

    private readonly List<Payment> _payments = new();
    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    /// <summary>Outstanding balance = TTC − collected (never negative).</summary>
    public decimal Outstanding => Math.Max(0m, TotalTtc - AmountCollected);

    /// <summary>Only a draft can be deleted; an issued invoice is cancelled instead.</summary>
    public bool CanBeDeleted => Status == InvoiceStatus.Draft;

    /// <summary>
    /// Whether this note can be corrected — replaced by a new one carrying the same money.
    ///
    /// <para>Distinct from <c>CanCreateCreditNote</c>, and the distinction is the whole point: an <b>avoir</b>
    /// records that money went <i>back to the patient</i>. A mis-keyed amount gave nothing back, so an avoir
    /// there states a refund that never happened. Correcting cancels the wrong note and raises the right one,
    /// which is what actually occurred.</para>
    ///
    /// <para>Already superseded is excluded: a note is corrected once, and the correction is corrected next.</para>
    /// </summary>
    public bool CanBeCorrected =>
        Status != InvoiceStatus.Draft && Status != InvoiceStatus.Cancelled && SupersededByInvoiceId is null;

    /// <summary>
    /// Point this note at the one replacing it, and vice versa. Called on both sides of a correction so neither
    /// end is a dead end.
    /// </summary>
    public void MarkSupersededBy(Guid replacementId)
    {
        if (replacementId == Guid.Empty)
            throw new ArgumentException("La note de remplacement est requise.", nameof(replacementId));
        if (replacementId == Id)
            throw new ArgumentException("Une note ne peut pas se remplacer elle-même.", nameof(replacementId));

        SupersededByInvoiceId = replacementId;
        Touch();
    }

    /// <inheritdoc cref="SupersedesInvoiceId"/>
    public void MarkSupersedes(Guid correctedId, string reason)
    {
        if (correctedId == Guid.Empty)
            throw new ArgumentException("La note corrigée est requise.", nameof(correctedId));
        if (correctedId == Id)
            throw new ArgumentException("Une note ne peut pas se remplacer elle-même.", nameof(correctedId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif de la correction est requis.", nameof(reason));

        SupersedesInvoiceId = correctedId;
        SupersedesReason = reason.Trim();
        Touch();
    }

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
    /// Emit the draft: assign its (externally computed, unique) sequential number, recompute the totals, and
    /// move it to Issued. Requires at least one line.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>It no longer takes the clinic's VAT/stamp settings, because there are none to freeze</b> — an act's
    /// price is the whole of what the patient owes. The three tax columns stay on the entity so a note issued
    /// before this change still renders with the figures it was really issued with; a new invoice simply leaves
    /// them at their zero defaults. Dropping the parameters rather than passing zeroes is deliberate: it makes
    /// « issue this invoice with a tax on top » unsayable, so no future caller can reintroduce the divergence
    /// between the fiche de soins' total and the note it generates.
    /// </remarks>
    public void Issue(string number)
    {
        EnsureDraft();

        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Le numéro de facture est requis.", nameof(number));

        if (_lines.Count == 0)
            throw new InvalidOperationException("Une facture doit comporter au moins une ligne pour être émise.");

        Number = number.Trim();
        IssueDate = DateTime.UtcNow;
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
    /// <param name="cheque">
    /// The cheque's number, bank and due date (L8) — required to be null for any method other than
    /// <see cref="PaymentMethod.Cheque"/>, which <see cref="ChequeDetails.For"/> enforces before this is reached.
    /// </param>
    public void RecordPayment(
        decimal amount,
        PaymentMethod method,
        DateTime paidOn,
        Guid? sourceInstallmentPaymentId = null,
        ChequeDetails? cheque = null,
        ChequeBankedStamp? banked = null)
    {
        if (Status != InvoiceStatus.Issued && Status != InvoiceStatus.PartiallyPaid)
            throw new InvalidOperationException("Un paiement ne peut être enregistré que sur une facture émise.");

        // Round to the millime first: an amount below half a millime would otherwise be accepted here and
        // stored as 0,000 by the decimal(18,3) column — a zero-amount payment row that counts for nothing but
        // blocks cancellation forever.
        var rounded = InvoiceCalculator.RoundMoney(amount);
        if (rounded <= 0)
            throw new ArgumentException("Le montant du paiement doit être d'au moins 1 millime.", nameof(amount));

        if (InvoiceCalculator.RoundMoney(AmountCollected + rounded) > TotalTtc)
            throw new InvalidOperationException("Le paiement dépasse le montant restant dû.");

        _payments.Add(new Payment(
            Guid.NewGuid(), Id, rounded, method, paidOn, sourceInstallmentPaymentId, cheque, banked));
        RecomputeCollected();
        Touch();
    }

    /// <summary>
    /// Void a recorded payment — "this was never received". The row is kept and marked, never deleted, so the
    /// correction leaves a trail; <see cref="AmountCollected"/> is recomputed and the status walks back
    /// (Paid → PartiallyPaid → Issued).
    /// </summary>
    /// <param name="creditedTotal">
    /// Σ of the non-cancelled avoirs already issued against this invoice. Passed in by the handler because the
    /// aggregate has no repository access. Collected cash may not fall below money the clinic has already
    /// refunded on paper, or the same dinar leaves the caisse twice — once as the avoir, once as the void.
    /// </param>
    /// <remarks>A void is a correction, not a refund. Money actually returned is a <see cref="CreditNote"/>.</remarks>
    public void VoidPayment(
        Guid paymentId,
        string reason,
        decimal creditedTotal,
        string? actorUserId = null,
        string? actorName = null)
    {
        if (Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("La facture est annulée : ses paiements ne peuvent plus être modifiés.");

        var payment = _payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("Paiement introuvable sur cette facture.");

        if (payment.IsVoided)
            throw new InvalidOperationException("Ce paiement est déjà annulé.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif d'annulation du paiement est requis.", nameof(reason));

        var remaining = InvoiceCalculator.RoundMoney(AmountCollected - payment.Amount);
        if (remaining < InvoiceCalculator.RoundMoney(creditedTotal))
            throw new InvalidOperationException(
                "Le montant encaissé ne peut pas descendre en dessous des avoirs déjà établis sur cette facture.");

        payment.Void(reason, actorUserId, actorName);
        RecomputeCollected();
        Touch();
    }

    /// <summary>
    /// Mark one of this invoice's cheque payments as banked, or take the mark back (Group B).
    ///
    /// <para>⚠️ <b>No figure moves.</b> Unlike <see cref="VoidPayment"/> there is no <c>RecomputeCollected()</c>
    /// here and there must never be one: banking is a tracking state, la caisse counts a cheque on the day it was
    /// received, and re-dating collected cash on clearing would move every historical figure the practice has
    /// already read and reconciled. <see cref="Touch"/> is called all the same — the audit interceptor records
    /// <b>aggregate roots</b>, so without it a mark and an un-mark would leave no trail at all.</para>
    /// </summary>
    /// <param name="banked">True to stamp it, false to clear the stamp — a cheque returned unpaid by the bank.</param>
    public void SetPaymentBanked(
        Guid paymentId,
        bool banked,
        string? actorUserId = null,
        string? actorName = null)
    {
        var payment = _payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("Paiement introuvable sur cette facture.");

        // A voided payment was never received, so there is no cheque to take anywhere — and AC-12 requires it gone
        // from every cheque view whatever its banked state, which a stamp applied afterwards would contradict.
        if (payment.IsVoided)
            throw new InvalidOperationException("Ce paiement est annulé : il ne détient plus de chèque à encaisser.");

        if (payment.ChequeBankedOn.HasValue == banked)
            throw new InvalidOperationException(
                banked
                    ? "Ce chèque est déjà marqué comme encaissé en banque."
                    : "Ce chèque n'est pas marqué comme encaissé en banque.");

        payment.SetBanked(banked, actorUserId, actorName);
        Touch();
    }

    /// <summary>
    /// Move one live payment's <c>PaidOn</c> — the correction a backdated fiche de soins needs (L4).
    ///
    /// <para>Refused on a banked cheque: that row is reconciled against a bank statement, and moving its date
    /// would make the two disagree with nothing on screen to say so. Refused on a voided payment too — it is
    /// already out of every total, so there is nothing to move. Neither refusal touches the note itself, which
    /// legitimately keeps the day it was written.</para>
    /// </summary>
    public void AmendPaymentDate(Guid paymentId, DateTime paidOn)
    {
        if (Status == InvoiceStatus.Cancelled)
            throw new InvalidOperationException("La facture est annulée : ses paiements ne peuvent plus être modifiés.");

        var payment = _payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("Paiement introuvable sur cette facture.");

        if (payment.IsVoided)
            throw new InvalidOperationException("Ce paiement est annulé : sa date n'a plus d'effet.");

        if (payment.ChequeBankedOn is not null)
            throw new InvalidOperationException(
                "Ce chèque est déjà déposé : sa date d'encaissement est rapprochée avec la banque et ne peut plus "
                + "être déplacée. Retirez la marque de dépôt d'abord.");

        payment.AmendPaidOn(paidOn);
        Touch();
    }

    /// <summary>
    /// Derive <see cref="AmountCollected"/> and the payment status from the live (non-voided) payment rows.
    ///
    /// <para>
    /// Deliberately a recompute rather than an increment/decrement. <c>AmountCollected</c> is a stored column
    /// while the caisse sums the payment rows, and nothing has ever reconciled the two — so any historical
    /// drift is invisible in the app. Recomputing makes the arithmetic unfalsifiable, and the payments are
    /// always loaded with the invoice anyway.
    /// </para>
    /// </summary>
    private void RecomputeCollected()
    {
        AmountCollected = InvoiceCalculator.RoundMoney(_payments.Where(p => !p.IsVoided).Sum(p => p.Amount));

        // A cancelled invoice keeps its status; otherwise it follows the money.
        if (Status == InvoiceStatus.Cancelled || Status == InvoiceStatus.Draft)
        {
            return;
        }

        Status = AmountCollected <= 0m
            ? InvoiceStatus.Issued
            : AmountCollected >= TotalTtc
                ? InvoiceStatus.Paid
                : InvoiceStatus.PartiallyPaid;
    }

    /// <summary>
    /// True when this invoice may be cancelled: issued, not already cancelled, and carrying no <b>live</b>
    /// payment. Purely fiscal — no external declaration state can hold a cancellation any more.
    /// </summary>
    public bool CanCancel =>
        Status != InvoiceStatus.Draft
        && Status != InvoiceStatus.Cancelled
        && !_payments.Any(p => !p.IsVoided);

    /// <summary>True when an avoir may be established: the invoice is issued and has collected money to credit.</summary>
    public bool CanCreateCreditNote =>
        Status != InvoiceStatus.Draft && Status != InvoiceStatus.Cancelled && AmountCollected > 0m;

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

        // A note with LIVE payments must not be silently voided — that would erase collected cash from the
        // caisse with no trail. Corrections go through an avoir. Voided payments do not count: a note whose
        // only payments were data-entry errors was never really paid, so cancelling it is legitimate.
        if (_payments.Any(p => !p.IsVoided))
            // The avoir is not *a* route, it is the only one — worth saying, because on a devis→facture bridge the
            // plan's collections were carried onto this invoice at issue, so users reach for « annuler » expecting
            // the money to go back to the devis. It cannot: the carry is one-way and one-time.
            throw new InvalidOperationException(
                "Une facture avec des paiements enregistrés ne peut pas être annulée : établissez un avoir, "
                + "seul moyen de rendre l'argent. Pour une facture issue d'un devis, les encaissements du devis "
                + "y ont été reportés à l'émission et ne repartent pas en arrière.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif d'annulation est requis.", nameof(reason));

        CancellationReason = reason.Trim();
        Status = InvoiceStatus.Cancelled;
        Touch();
    }

    private void RecomputeTotals()
    {
        var ht = _lines.Sum(l => l.LineTotalHt);
        var totals = InvoiceCalculator.Compute(ht);
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
