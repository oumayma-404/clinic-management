using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One received lot of a <see cref="StockItem"/> — the quantity that arrived together, with the expiry date and
/// batch number that belong to <i>that</i> delivery (AC-P4.1).
///
/// This exists because <c>StockItem.ExpiryDate</c>/<c>BatchNumber</c> were two scalar columns that
/// <c>AddStock</c> <b>overwrote</b>: a second restock silently destroyed the first lot's date, so the item
/// displayed the expiry of whatever arrived last rather than the one that actually matters. An earlier draft of
/// the spec claimed the entity "already models" this; it did not, which is why per-batch rows are a schema
/// change.
///
/// Both fields stay optional (AC-P4.7): a clinic that does not track expiry gets one batch per delivery with
/// nulls, and every read behaves exactly as before.
/// </summary>
public class StockBatch : Entity<Guid>
{
    public Guid StockItemId { get; private set; }

    /// <summary>What arrived in this lot. Immutable — <see cref="RemainingQuantity"/> is what moves.</summary>
    public int ReceivedQuantity { get; private set; }

    /// <summary>
    /// What is left of this lot. Drawn down FEFO by <see cref="StockItem.ConsumeStock"/>. Never negative: a
    /// shortfall beyond every batch is carried on the item's own <c>CurrentStock</c>, which AC-P4.12 allows to
    /// go negative rather than silently clamping and losing the discrepancy.
    /// </summary>
    public int RemainingQuantity { get; private set; }

    public DateTime? ExpiryDate { get; private set; }
    public string? BatchNumber { get; private set; }
    public DateTime ReceivedAt { get; private set; }

    private StockBatch() { } // For EF Core

    public StockBatch(
        Guid id,
        Guid stockItemId,
        int receivedQuantity,
        DateTime? expiryDate = null,
        string? batchNumber = null,
        DateTime? receivedAt = null)
    {
        if (stockItemId == Guid.Empty)
            throw new ArgumentException("L'article de stock est requis.", nameof(stockItemId));
        if (receivedQuantity <= 0)
            throw new ArgumentException("La quantité reçue doit être supérieure à 0.", nameof(receivedQuantity));

        Id = id;
        StockItemId = stockItemId;
        ReceivedQuantity = receivedQuantity;
        RemainingQuantity = receivedQuantity;
        ExpiryDate = expiryDate;
        BatchNumber = string.IsNullOrWhiteSpace(batchNumber) ? null : batchNumber.Trim();
        // The backfill needs to date an opening batch as of the item's creation, not of the migration run.
        ReceivedAt = receivedAt ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Takes up to <paramref name="quantity"/> from this lot and returns how much it could actually give.
    /// The caller moves on to the next batch with the remainder — that loop is FEFO order (AC-P4.4).
    /// </summary>
    public int Draw(int quantity)
    {
        if (quantity <= 0)
            return 0;

        var drawn = Math.Min(quantity, RemainingQuantity);
        RemainingQuantity -= drawn;
        return drawn;
    }

    /// <summary>True when this lot still holds stock and its expiry has passed (or is today).</summary>
    public bool IsExpired(DateTime asOfUtc) =>
        RemainingQuantity > 0 && ExpiryDate.HasValue && ExpiryDate.Value.Date <= asOfUtc.Date;

    /// <summary>True when this lot still holds stock and expires within <paramref name="leadDays"/>.</summary>
    public bool IsExpiringSoon(DateTime asOfUtc, int leadDays) =>
        RemainingQuantity > 0
        && ExpiryDate.HasValue
        && ExpiryDate.Value.Date > asOfUtc.Date
        && ExpiryDate.Value.Date <= asOfUtc.Date.AddDays(leadDays);
}
