using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A payment recorded against an <see cref="Invoice"/> (aggregate child).
///
/// <para>
/// The amount, method and date are immutable once created — a payment is never edited. It can, however, be
/// <b>voided</b>: the row is kept and marked, never deleted, so the correction leaves a trail. That is the
/// difference between correcting a data-entry error and pretending it never happened.
/// </para>
/// <para>
/// A void is not a refund. Money actually returned to the patient is a <see cref="CreditNote"/> (avoir), which
/// is a numbered fiscal document. Voiding says "this payment was never received".
/// </para>
/// </summary>
public class Payment : Entity<Guid>
{
    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public DateTime PaidOn { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>True once this payment has been reversed. Voided payments are excluded from every cash read.</summary>
    public bool IsVoided { get; private set; }

    /// <summary>When the void was performed — system time, not the money date.</summary>
    public DateTime? VoidedAt { get; private set; }

    /// <summary>Why it was voided. Required by the command; the reason is shown wherever the payment is.</summary>
    public string? VoidReason { get; private set; }

    /// <summary>
    /// Who voided it. A soft link — no foreign key — because <c>User.Id</c> is a string and users get
    /// deactivated; the trail must survive that. This is the first place in the codebase that records who
    /// mutated a financial document.
    /// </summary>
    public string? VoidedByUserId { get; private set; }

    /// <summary>Name snapshot of the actor, so reading the trail needs no user lookup.</summary>
    public string? VoidedByName { get; private set; }

    /// <summary>
    /// Set when this payment was carried onto the invoice from a treatment-plan installment by the
    /// devis→facture bridge, rather than collected directly against the invoice.
    ///
    /// <para>
    /// This is the de-duplication discriminator: without it the caisse would count the same dinar twice, once
    /// on the plan track and once on the invoice track. A soft link (no foreign key), populated by the bridge.
    /// </para>
    /// </summary>
    public Guid? SourceInstallmentPaymentId { get; private set; }

    /// <summary>
    /// The cheque's number, when <see cref="Method"/> is <see cref="PaymentMethod.Cheque"/> (L8). Null for every
    /// other method — the invariant is enforced once, in <see cref="ChequeDetails.For"/>.
    /// </summary>
    public string? ChequeNumber { get; private set; }

    /// <inheritdoc cref="ChequeDetails.BankName"/>
    public string? ChequeBankName { get; private set; }

    /// <inheritdoc cref="ChequeDetails.DueDate"/>
    public DateTime? ChequeDueDate { get; private set; }

    private Payment() { } // For EF Core

    public Payment(
        Guid id,
        Guid invoiceId,
        decimal amount,
        PaymentMethod method,
        DateTime paidOn,
        Guid? sourceInstallmentPaymentId = null,
        ChequeDetails? cheque = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Le montant du paiement doit être supérieur à 0.", nameof(amount));

        Id = id;
        InvoiceId = invoiceId;
        Amount = amount;
        Method = method;
        PaidOn = paidOn;
        SourceInstallmentPaymentId = sourceInstallmentPaymentId;
        // Flattened into three columns rather than kept as an owned type: the number is searched and the due date
        // is sorted on, both on their own. See `ChequeDetails` for why the rule still lives in exactly one place.
        ChequeNumber = cheque?.Number;
        ChequeBankName = cheque?.BankName;
        ChequeDueDate = cheque?.DueDate;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Mark this payment as never received. Idempotent by contract — the caller (<see cref="Invoice.VoidPayment"/>)
    /// refuses a second void, so a double-click cannot rewrite the original reason or actor.
    /// </summary>
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
