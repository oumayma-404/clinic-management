using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

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

    /// <summary>
    /// The cheque's number, when <see cref="Method"/> is <see cref="PaymentMethod.Cheque"/> (L8). Mirrors
    /// <see cref="Payment"/>'s three columns — an échéance is as often settled by post-dated cheque as an invoice
    /// is, and a view of cheques to bank that saw only one of the two ledgers would be worse than none.
    /// </summary>
    public string? ChequeNumber { get; private set; }

    /// <inheritdoc cref="ChequeDetails.BankName"/>
    public string? ChequeBankName { get; private set; }

    /// <inheritdoc cref="ChequeDetails.DueDate"/>
    public DateTime? ChequeDueDate { get; private set; }

    /// <inheritdoc cref="Payment.ChequeBankedOn"/>
    public DateTime? ChequeBankedOn { get; private set; }

    /// <inheritdoc cref="Payment.ChequeBankedByUserId"/>
    public string? ChequeBankedByUserId { get; private set; }

    /// <inheritdoc cref="Payment.ChequeBankedByName"/>
    public string? ChequeBankedByName { get; private set; }

    private InstallmentPayment() { } // For EF Core

    public InstallmentPayment(
        Guid id,
        Guid installmentId,
        decimal amount,
        PaymentMethod method,
        DateTime paidOn,
        ChequeDetails? cheque = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant du paiement doit être supérieur à 0.", nameof(amount));

        Id = id;
        InstallmentId = installmentId;
        Amount = amount;
        Method = method;
        PaidOn = paidOn;
        ChequeNumber = cheque?.Number;
        ChequeBankName = cheque?.BankName;
        ChequeDueDate = cheque?.DueDate;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The details as a value object again — for the devis→facture bridge, which must carry this cheque onto the
    /// invoice's own payment row.
    ///
    /// <para>⚠️ Without it a bridged cheque loses its number, its bank and its due date, and therefore disappears
    /// from any « chèques à encaisser » view: the plan side stops being counted the moment the bridge invoice is
    /// issued, and the invoice side would hold a cheque nobody can identify. Rebuilt through
    /// <see cref="ChequeDetails.For"/> rather than copied field-by-field, so the method/details invariant is
    /// re-checked on the way across instead of being trusted.</para>
    /// </summary>
    public ChequeDetails? ToChequeDetails() =>
        ChequeDetails.For(Method, ChequeNumber, ChequeBankName, ChequeDueDate);

    /// <summary>
    /// The banked mark as a value object again — for the same one caller, and for the same reason one field over.
    ///
    /// <para>⚠️ Once the bridge invoice is issued the plan side stops being counted, so a stamp left behind here
    /// does not merely go missing: the cheque <b>reappears</b> under « à encaisser » although it is physically at
    /// the bank, and re-marking it would record today rather than the day it was actually deposited. Rebuilt
    /// through <see cref="ChequeBankedStamp.For"/> so the method invariant is re-checked on the way across.</para>
    /// </summary>
    public ChequeBankedStamp? ToBankedStamp() =>
        ChequeBankedStamp.For(Method, ChequeBankedOn, ChequeBankedByUserId, ChequeBankedByName);

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

    /// <inheritdoc cref="Payment.SetBanked"/>
    internal void SetBanked(bool banked, string? actorUserId, string? actorName)
    {
        if (Method != PaymentMethod.Cheque)
            throw new InvalidOperationException("Seul un règlement par chèque peut être marqué comme encaissé en banque.");

        ChequeBankedOn = banked ? DateTime.UtcNow : null;
        ChequeBankedByUserId = banked ? actorUserId : null;
        ChequeBankedByName = banked ? actorName : null;
    }
}
