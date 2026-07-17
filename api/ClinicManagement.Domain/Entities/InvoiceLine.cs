using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A single billable act line on an <see cref="Invoice"/> (aggregate child). Holds only the act
/// designation, quantity and unit price HT — never a diagnosis/pathology (medical secrecy).
/// </summary>
public class InvoiceLine : Entity<Guid>
{
    public Guid InvoiceId { get; private set; }
    public string Designation { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPriceHt { get; private set; }
    public decimal LineTotalHt { get; private set; }

    private InvoiceLine() { } // For EF Core

    public InvoiceLine(Guid id, Guid invoiceId, string designation, int quantity, decimal unitPriceHt)
    {
        if (string.IsNullOrWhiteSpace(designation))
            throw new ArgumentException("La désignation de la ligne est requise.", nameof(designation));

        if (quantity <= 0)
            throw new ArgumentException("La quantité doit être supérieure à 0.", nameof(quantity));

        if (unitPriceHt < 0)
            throw new ArgumentException("Le prix unitaire HT ne peut pas être négatif.", nameof(unitPriceHt));

        Id = id;
        InvoiceId = invoiceId;
        Designation = designation.Trim();
        Quantity = quantity;
        UnitPriceHt = unitPriceHt;
        LineTotalHt = InvoiceCalculator.LineTotal(quantity, unitPriceHt);
    }
}
