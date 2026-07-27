using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One payment received against an <see cref="Installment"/> — a real row per event, mirroring
/// <see cref="Payment"/> on the invoice side.
///
/// <para>
/// Before this existed, an installment kept only a running <c>AmountPaid</c> plus the <b>latest</b> method and
/// date. That made three things wrong at once: money was attributed entirely to the last payment's month (400
/// DT in January and 600 in February reported 0 then 1000, and January's figure changed <i>retroactively</i>),
/// a second receipt reprinted the cumulative sum, and a mistyped payment could never be corrected.
/// </para>
/// <para>
/// Voidable on the same terms as an invoice payment: the row is kept and marked, never deleted.
/// </para>
/// </summary>
public class InstallmentPayment : Entity<Guid>
{
    public Guid InstallmentId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }

    /// <summary>
    /// The date the money was received — the bucketing key for every cash read, and the entire point of this
    /// entity existing.
    /// </summary>
    public DateTime PaidOn { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public bool IsVoided { get; private set; }
    public DateTime? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>Soft link — no foreign key, because <c>User.Id</c> is a string and users get deactivated.</summary>
    public string? VoidedByUserId { get; private set; }
    public string? VoidedByName { get; private set; }

    private InstallmentPayment() { } // For EF Core

    public InstallmentPayment(Guid id, Guid installmentId, decimal amount, PaymentMethod method, DateTime paidOn)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant du paiement doit être supérieur à 0.", nameof(amount));

        Id = id;
        InstallmentId = installmentId;
        Amount = amount;
        Method = method;
        PaidOn = paidOn;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Mark this payment as never received. The caller refuses a second void, so it cannot be rewritten.</summary>
    internal void Void(string reason, string? actorUserId, string? actorName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif d'annulation du paiement est requis.", nameof(reason));

        IsVoided = true;
        VoidedAt = DateTime.UtcNow;
        VoidReason = reason.Trim();
        VoidedByUserId = actorUserId;
        VoidedByName = actorName;
    }
}
