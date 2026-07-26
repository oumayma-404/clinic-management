using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A scheduled installment (échéance) of a <see cref="TreatmentPlan"/>'s payment plan (aggregate child).
/// Payments accumulate into <see cref="AmountPaid"/> (v1 keeps only the latest method/date, not a full
/// payment history). Overpayment beyond <see cref="Amount"/> is refused. All money in TND millimes.
/// </summary>
public class Installment : Entity<Guid>
{
    public Guid TreatmentPlanId { get; private set; }
    public DateTime DueDate { get; private set; }
    public decimal Amount { get; private set; }
    public decimal AmountPaid { get; private set; }
    public PaymentMethod? LastMethod { get; private set; }
    public DateTime? LastPaidOn { get; private set; }

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

    public void RecordPayment(decimal amount, PaymentMethod method, DateTime paidOn)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant du paiement doit être supérieur à 0.", nameof(amount));
        if (AmountPaid + amount > Amount)
            throw new InvalidOperationException("Le paiement dépasse le montant restant dû de l'échéance.");

        AmountPaid = InvoiceCalculator.RoundMoney(AmountPaid + amount);
        LastMethod = method;
        LastPaidOn = paidOn;
    }
}
