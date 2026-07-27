using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

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

    public Installment(Guid id, Guid treatmentPlanId, DateTime dueDate, decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant de l'échéance doit être supérieur à 0.", nameof(amount));

        Id = id;
        TreatmentPlanId = treatmentPlanId;
        DueDate = dueDate;
        Amount = InvoiceCalculator.RoundMoney(amount);
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
    }

    /// <summary>Record a payment as its own ledger row, then re-derive the stored totals from the ledger.</summary>
    public InstallmentPayment RecordPayment(decimal amount, PaymentMethod method, DateTime paidOn)
    {
        // Round first: a sub-millime amount would otherwise be stored as 0,000 by the decimal(18,3) column.
        var rounded = InvoiceCalculator.RoundMoney(amount);
        if (rounded <= 0)
            throw new ArgumentException("Le montant du paiement doit être d'au moins 1 millime.", nameof(amount));

        if (InvoiceCalculator.RoundMoney(AmountPaid + rounded) > Amount)
            throw new InvalidOperationException("Le paiement dépasse le montant restant dû de l'échéance.");

        var payment = new InstallmentPayment(Guid.NewGuid(), Id, rounded, method, paidOn);
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
