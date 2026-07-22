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

    /// <summary>
    /// Optional soft link to the dental record this line bills. Lets the "already invoiced" guard exclude a
    /// record even on a multi-record note d'honoraires (the invoice header carries at most one record link).
    /// </summary>
    public Guid? DentalRecordId { get; private set; }

    /// <summary>
    /// Optional link to the catalog CNAM/DCH act this line bills (mirrors <see cref="TreatmentPlanItem"/>).
    /// Drives the indicative CNAM-reimbursable vs. patient-out-of-pocket split on the invoice; a line with
    /// no act (free-text honoraires) is counted fully out-of-pocket.
    /// </summary>
    public Guid? DentalActCodeId { get; private set; }

    /// <summary>Snapshot of the DCH code (e.g. <c>DCH020030</c>) at billing time, for display.</summary>
    public string? CodeActe { get; private set; }

    private InvoiceLine() { } // For EF Core

    public InvoiceLine(
        Guid id,
        Guid invoiceId,
        string designation,
        int quantity,
        decimal unitPriceHt,
        Guid? dentalRecordId = null,
        Guid? dentalActCodeId = null,
        string? codeActe = null)
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
        DentalRecordId = dentalRecordId;
        DentalActCodeId = dentalActCodeId;
        CodeActe = string.IsNullOrWhiteSpace(codeActe) ? null : codeActe.Trim();
    }
}
