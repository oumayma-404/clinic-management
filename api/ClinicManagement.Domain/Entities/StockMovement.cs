using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// An append-only stock movement (finding #14): one row per consume/restock, recording the delta and the
/// resulting on-hand quantity so the inventory has an audit trail instead of only an absolute overwrite.
/// Clinic-scoped; references its <see cref="StockItemId"/> (soft link).
/// </summary>
public class StockMovement : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid StockItemId { get; private set; }
    public StockMovementType Type { get; private set; }
    public int Quantity { get; private set; }

    /// <summary>The item's on-hand quantity immediately after this movement was applied.</summary>
    public int ResultingStock { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private StockMovement() { } // For EF Core

    public StockMovement(Guid id, Guid clinicId, Guid stockItemId, StockMovementType type, int quantity, int resultingStock, string? reason = null)
    {
        if (clinicId == Guid.Empty)
            throw new ArgumentException("Le cabinet est requis.", nameof(clinicId));
        if (stockItemId == Guid.Empty)
            throw new ArgumentException("L'article de stock est requis.", nameof(stockItemId));
        if (quantity <= 0)
            throw new ArgumentException("La quantité doit être supérieure à 0.", nameof(quantity));

        Id = id;
        ClinicId = clinicId;
        StockItemId = stockItemId;
        Type = type;
        Quantity = quantity;
        ResultingStock = resultingStock;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        CreatedAt = DateTime.UtcNow;
    }
}
