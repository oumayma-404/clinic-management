namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Lifecycle of an <see cref="Entities.Invoice"/>: Draft (no number, editable, deletable) →
/// Issued (numbered + totals frozen) → PartiallyPaid / Paid, or Cancelled (number kept, no more payments).
/// </summary>
public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Cancelled = 4
}
