using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A payment recorded against an <see cref="Invoice"/> (aggregate child). Immutable once created.
/// </summary>
public class Payment : Entity<Guid>
{
    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public DateTime PaidOn { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Payment() { } // For EF Core

    public Payment(Guid id, Guid invoiceId, decimal amount, PaymentMethod method, DateTime paidOn)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant du paiement doit être supérieur à 0.", nameof(amount));

        Id = id;
        InvoiceId = invoiceId;
        Amount = amount;
        Method = method;
        PaidOn = paidOn;
        CreatedAt = DateTime.UtcNow;
    }
}
