using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// An avoir (credit note): a dated, numbered document that offsets some or all of a paid/partially-paid
/// invoice's collected amount (aggregate root, clinic-scoped). It is the lawful correction path for a
/// note d'honoraires whose cash was already received — the invoice keeps its number/status and the avoir
/// records the reversal, so the caisse/recettes reflect the net instead of a silent deletion. Its number
/// uses its own per-clinic-per-year <c>AAAA-NNNN</c> sequence (separate from invoices and treatment plans).
/// Money in TND millimes.
/// </summary>
public class CreditNote : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid InvoiceId { get; private set; }

    /// <summary>Sequential number <c>AAAA-NNNN</c> (own per-clinic-per-year sequence).</summary>
    public string Number { get; private set; } = string.Empty;
    public DateTime IssueDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public PaymentMethod? Method { get; private set; }

    /// <summary>When the money went back to the patient — the date the caisse/recettes nets it against.</summary>
    public DateTime RefundedOn { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private CreditNote() { } // For EF Core

    public CreditNote(
        Guid id, Guid clinicId, Guid invoiceId, string number, decimal amount,
        string reason, PaymentMethod? method, DateTime refundedOn)
    {
        if (clinicId == Guid.Empty)
            throw new ArgumentException("Le cabinet est requis.", nameof(clinicId));
        if (invoiceId == Guid.Empty)
            throw new ArgumentException("La facture est requise.", nameof(invoiceId));
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Le numéro de l'avoir est requis.", nameof(number));
        if (amount <= 0)
            throw new ArgumentException("Le montant de l'avoir doit être supérieur à 0.", nameof(amount));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif de l'avoir est requis.", nameof(reason));

        Id = id;
        ClinicId = clinicId;
        InvoiceId = invoiceId;
        Number = number.Trim();
        Amount = InvoiceCalculator.RoundMoney(amount);
        Reason = reason.Trim();
        Method = method;
        RefundedOn = refundedOn;
        IssueDate = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Reassign the number — only to resolve a concurrent numbering collision during creation.</summary>
    public void SetNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Le numéro de l'avoir est requis.", nameof(number));
        Number = number.Trim();
    }
}
