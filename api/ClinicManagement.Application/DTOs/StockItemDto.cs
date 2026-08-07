using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

public class StockItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int CurrentStock { get; set; }
    public int MinimumStockLevel { get; set; }
    public int MaximumStockLevel { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Supplier { get; set; }
    public bool IsLowStock { get; set; }

    /// <summary>
    /// The lots on the shelf, soonest-expiry first (AC-P4.3). Replaces the two scalar `expiryDate`/`batchNumber`
    /// fields the item used to expose, which showed whatever arrived <i>last</i> rather than the date that
    /// actually matters.
    /// </summary>
    public List<StockBatchDto> Batches { get; set; } = new();

    /// <summary>
    /// The expiry the table shows: the soonest one still holding stock (AC-P4.3/4.5). Null when nothing on the
    /// shelf carries a date — an item whose lots have no expiry reads exactly as it did before (AC-P4.7).
    /// </summary>
    public DateTime? EarliestExpiry { get; set; }

    /// <summary>True when a lot still on the shelf is at or past its expiry (AC-P4.5). Drives the row highlight.</summary>
    public bool HasExpiredStock { get; set; }

    /// <summary>True when a lot still on the shelf expires inside the configured lead time (AC-P4.6).</summary>
    public bool IsExpiringSoon { get; set; }

    /// <summary>
    /// The row's optimistic-concurrency token (AC-P4.18). Echoed back on update so a concurrent consume is
    /// refused with a 409 instead of being silently overwritten — this is the inherited `Entity&lt;TId&gt;.Version`
    /// mapped onto `xmin`, deliberately not a second mechanism.
    /// </summary>
    public uint Version { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>One received lot of a stock item, as read by the client (AC-P4.3).</summary>
public class StockBatchDto
{
    public Guid Id { get; set; }
    public int ReceivedQuantity { get; set; }
    public int RemainingQuantity { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime ReceivedAt { get; set; }

    /// <summary>At or past its expiry date, and still holding stock.</summary>
    public bool IsExpired { get; set; }
}

public static class StockItemMappingExtensions
{
    /// <summary>
    /// Maps an item plus its lots. <paramref name="expiryLeadDays"/> is the approaching-expiry lead time
    /// (AC-P4.6) — passed in rather than read here, because the DTO layer has no configuration and the same
    /// item can legitimately be rendered against a different lead time by a different caller.
    /// </summary>
    public static StockItemDto ToDto(this StockItem item, int expiryLeadDays = 0, DateTime? asOfUtc = null)
    {
        var now = asOfUtc ?? DateTime.UtcNow;

        return new StockItemDto
        {
            Id = item.Id,
            Name = item.Name,
            Description = item.Description,
            Category = item.Category,
            Unit = item.Unit,
            CurrentStock = item.CurrentStock,
            MinimumStockLevel = item.MinimumStockLevel,
            MaximumStockLevel = item.MaximumStockLevel,
            UnitPrice = item.UnitPrice,
            Supplier = item.Supplier,
            IsLowStock = item.IsLowStock(),
            // Soonest-expiry first, so the client never has to re-derive FEFO order to show the relevant lot.
            Batches = item.Batches
                .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(b => b.ReceivedAt)
                .Select(b => new StockBatchDto
                {
                    Id = b.Id,
                    ReceivedQuantity = b.ReceivedQuantity,
                    RemainingQuantity = b.RemainingQuantity,
                    ExpiryDate = b.ExpiryDate,
                    BatchNumber = b.BatchNumber,
                    ReceivedAt = b.ReceivedAt,
                    IsExpired = b.IsExpired(now),
                })
                .ToList(),
            EarliestExpiry = item.EarliestRelevantExpiry(),
            HasExpiredStock = item.HasExpiredStock(now),
            IsExpiringSoon = expiryLeadDays > 0 && item.HasStockExpiringSoon(now, expiryLeadDays),
            Version = item.Version,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
        };
    }
}
