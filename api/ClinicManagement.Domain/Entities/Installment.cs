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
