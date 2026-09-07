using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A scheduled installment (échéance) of a <see cref="TreatmentPlan"/>'s payment plan (aggregate child).
///
/// <para>
/// Each payment is its own <see cref="InstallmentPayment"/> row, mirroring the invoice side.
/// <see cref="AmountPaid"/>, <see cref="LastMethod"/> and <see cref="LastPaidOn"/> remain as stored
/// denormalizations — thirteen read sites depend on them — but they are now <b>derived</b> from the ledger and
/// recomputed on every record and void. In particular <see cref="AmountPaid"/> is no longer monotonic, which
/// <see cref="Revise"/> and the plan's amendment rules both key off.
/// </para>
///
/// Overpayment beyond <see cref="Amount"/> is refused. All money in TND millimes.
/// </summary>
public class Installment : Entity<Guid>
{
    public Guid TreatmentPlanId { get; private set; }
    public DateTime DueDate { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>
    /// True when nobody agreed this date — the row was raised by the system so the money would have somewhere
    /// to live, not because a dentist and a patient settled on a day.
    ///
    /// <para>
    /// ⚠️ <b>This is the flag that makes « En retard » mean something again.</b> A devis accepted with no
    /// schedule gets ONE lump-sum row for the whole total dated at the acceptance instant
    /// (<see cref="TreatmentPlan.Accept"/>), so it is in the past by the time anyone looks and every plan in
    /// the database read « En retard » from the day after it was signed — measured at <b>25 of 27</b> unpaid
    /// échéances. The badge was not wrong about the date; the date was never a promise.
    /// </para>
    /// <para>
    /// The distinction has to be <b>stored</b> rather than guessed. A rule like « one row, dated at
    /// acceptance » would also match a schedule a dentist deliberately typed for the signature day, and
    /// silencing that one destroys the only thing an échéancier is for. See
    /// <see cref="Services.InstallmentLateness"/> for the single rule that reads it.
    /// </para>
    /// <para>
    /// <b>False by default</b>, so every row a human enters — <see cref="TreatmentPlan.SetInstallments"/> from
    /// the create form, <see cref="TreatmentPlan.ReviseInstallments"/> from « Modifier l'échéancier » — is an
    /// agreed date without any caller having to say so. Only the two system writers opt in.
    /// </para>
    /// </summary>
    public bool IsAutoRaised { get; private set; }

    /// <summary>Σ of the non-voided ledger rows. Stored, but always recomputed — never assigned directly.</summary>
    public decimal AmountPaid { get; private set; }

    /// <summary>Method of the most recent live payment. Derived; kept because existing reads use it.</summary>
    public PaymentMethod? LastMethod { get; private set; }

    /// <summary>Date of the most recent live payment. Derived; no longer the attribution key for cash reads.</summary>
    public DateTime? LastPaidOn { get; private set; }

    private readonly List<InstallmentPayment> _payments = new();
    public IReadOnlyCollection<InstallmentPayment> Payments => _payments.AsReadOnly();

    public decimal Outstanding => Math.Max(0m, Amount - AmountPaid);
    public bool IsPaid => AmountPaid >= Amount;

    private Installment() { } // For EF Core

    /// <param name="isAutoRaised">
    /// See <see cref="IsAutoRaised"/>. Defaults to false — a row nobody named is a row somebody typed, and the
    /// two system writers pass true explicitly.
    /// </param>
    public Installment(Guid id, Guid treatmentPlanId, DateTime dueDate, decimal amount, bool isAutoRaised = false)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant de l'échéance doit être supérieur à 0.", nameof(amount));

        Id = id;
        TreatmentPlanId = treatmentPlanId;
        DueDate = dueDate;
        Amount = InvoiceCalculator.RoundMoney(amount);
        IsAutoRaised = isAutoRaised;
    }

    /// <summary>
    /// Revise this échéance during an amendment. The amount can never drop below what has already been
    /// collected on it — money in the caisse cannot be un-received, and an installment whose
    /// <see cref="Amount"/> was under its <see cref="AmountPaid"/> would report a negative balance into
    /// « Créances ».
    /// </summary>
    public void Revise(DateTime dueDate, decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant de l'échéance doit être supérieur à 0.", nameof(amount));

        var rounded = InvoiceCalculator.RoundMoney(amount);
        if (rounded < AmountPaid)
        {
            throw new InvalidOperationException(
                $"Une échéance ne peut pas être ramenée en dessous du montant déjà encaissé ({AmountPaid:0.000} DT).");
        }

        DueDate = dueDate;
        Amount = rounded;
        // Revising is a dentist looking at the dates and settling on them, so whatever this row used to be it
        // is an agreed date now. Without this, the auto row survived « Modifier l'échéancier » still marked
        // auto and would never go red however deliberately its date had just been chosen.
        IsAutoRaised = false;
    }

    /// <summary>
    /// Mark a row as system-raised after the fact — used only by <c>RespreadSchedule</c>, which keeps the
    /// échéances that collected money and re-Revise()s them onto what they actually took. That call is
    /// bookkeeping, not an agreement, so it must not silently promote a row nobody scheduled.
    /// </summary>
    internal void MarkAutoRaised() => IsAutoRaised = true;

    /// <summary>Record a payment as its own ledger row, then re-derive the stored totals from the ledger.</summary>
    /// <param name="cheque">
    /// The cheque's number, bank and due date (L8). An échéance is settled by post-dated cheque at least as often
    /// as an invoice is, which is why both ledgers carry the fields rather than only the invoice side.
    /// </param>
    /// <param name="dentalRecordId">
    /// The fiche this money was handed over at, when it was collected chairside — see
    /// <see cref="InstallmentPayment.DentalRecordId"/>. Null for the till and échéancier routes.
    /// </param>
    public InstallmentPayment RecordPayment(
        decimal amount,
        PaymentMethod method,
        DateTime paidOn,
        ChequeDetails? cheque = null,
        Guid? dentalRecordId = null)
    {
        // Round first: a sub-millime amount would otherwise be stored as 0,000 by the decimal(18,3) column.
        var rounded = InvoiceCalculator.RoundMoney(amount);
        if (rounded <= 0)
            throw new ArgumentException("Le montant du paiement doit être d'au moins 1 millime.", nameof(amount));

        if (InvoiceCalculator.RoundMoney(AmountPaid + rounded) > Amount)
            throw new InvalidOperationException("Le paiement dépasse le montant restant dû de l'échéance.");

        var payment = new InstallmentPayment(Guid.NewGuid(), Id, rounded, method, paidOn, cheque, dentalRecordId);
        _payments.Add(payment);
        RecomputeFromLedger();
        return payment;
    }

    /// <summary>
    /// Void a recorded payment — "this was never received". The row is kept and marked; the stored totals are
    /// re-derived from the remaining live rows.
    /// </summary>
    public void VoidPayment(Guid paymentId, string reason, string? actorUserId, string? actorName)
    {
        var payment = _payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("Paiement introuvable sur cette échéance.");

        if (payment.IsVoided)
            throw new InvalidOperationException("Ce paiement est déjà annulé.");

        payment.Void(reason, actorUserId, actorName);
        RecomputeFromLedger();
    }

    /// <inheritdoc cref="Invoice.SetPaymentBanked"/>
    public void SetPaymentBanked(Guid paymentId, bool banked, string? actorUserId, string? actorName)
    {
        var payment = _payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new InvalidOperationException("Paiement introuvable sur cette échéance.");

        if (payment.IsVoided)
            throw new InvalidOperationException("Ce paiement est annulé : il ne détient plus de chèque à encaisser.");

        if (payment.ChequeBankedOn.HasValue == banked)
            throw new InvalidOperationException(
                banked
                    ? "Ce chèque est déjà marqué comme encaissé en banque."
                    : "Ce chèque n'est pas marqué comme encaissé en banque.");

        // Deliberately no `RecomputeFromLedger()`: banking moves no money, so `AmountPaid`, `LastMethod` and
        // `LastPaidOn` must all stay exactly where they are.
        payment.SetBanked(banked, actorUserId, actorName);
    }

    /// <summary>
    /// Re-derive the stored denormalizations from the live ledger rows. Called after every mutation so the
    /// two can never drift — the ledger is the truth, these are a cache of it.
    /// </summary>
    private void RecomputeFromLedger()
    {
        var live = _payments.Where(p => !p.IsVoided).ToList();

        AmountPaid = InvoiceCalculator.RoundMoney(live.Sum(p => p.Amount));

        // "Most recent" is by money date, with the insertion stamp as the tiebreaker — two payments on the
        // same day are common.
        var latest = live
            .OrderByDescending(p => p.PaidOn)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();

        LastMethod = latest?.Method;
        LastPaidOn = latest?.PaidOn;
    }

    /// <summary>
    /// Rebuild the stored totals from ledger rows loaded by EF (which bypasses the domain methods).
    ///
    /// Used only by the data migration's verification pass; the values it produces must equal what the
    /// backfill wrote, or the ledger and the denormalizations disagree from day one.
    /// </summary>
    internal void ResyncFromLedger() => RecomputeFromLedger();
}
