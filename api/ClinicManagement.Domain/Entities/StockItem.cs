using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

public class StockItem : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>
    /// Open text canonicalised through <see cref="StockCategories.Normalize"/> on every write — the six English
    /// storage keys this column used to hold were rewritten to their French labels by the suppliers migration.
    /// </summary>
    public string Category { get; private set; }
    public string Unit { get; private set; } // e.g., "Box", "Bottle", "Unit"
    public int CurrentStock { get; private set; }
    public int MinimumStockLevel { get; private set; }
    public int MaximumStockLevel { get; private set; }
    public decimal? UnitPrice { get; private set; }

    /// <summary>
    /// The <see cref="Entities.Supplier"/> this article is ordered from — at most one, and usually none.
    /// <para>
    /// ⚠️ It replaced a free-text <c>Supplier</c> string, whose whole defect was that it named somebody nobody
    /// could call. Nullable because « aucun fournisseur » is the common case (AC-5) and because refusing to
    /// record an article until somebody files its supplier would stop the stockroom working.
    /// </para>
    /// </summary>
    public Guid? SupplierId { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// The lots this item is made of, each carrying its own expiry and batch number (AC-P4.1). This replaces
    /// the two scalar <c>ExpiryDate</c>/<c>BatchNumber</c> columns that <c>AddStock</c> <b>overwrote</b> — a
    /// second delivery silently destroyed the first lot's date, so the item displayed whatever arrived last
    /// rather than the date that actually matters. The legacy scalars are folded into one opening batch by the
    /// migration (AC-P4.8), so nothing is dropped.
    /// </summary>
    private readonly List<StockBatch> _batches = new();
    public IReadOnlyCollection<StockBatch> Batches => _batches.AsReadOnly();

    private StockItem() { } // For EF Core

    public StockItem(
        Guid id,
        Guid clinicId,
        string name,
        string category,
        string unit,
        int minimumStockLevel,
        int maximumStockLevel,
        string? description = null,
        decimal? unitPrice = null,
        Guid? supplierId = null)
    {
        Id = id;
        ClinicId = clinicId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Category = RequireCategory(category);
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        MinimumStockLevel = minimumStockLevel;
        MaximumStockLevel = maximumStockLevel;
        Description = description;
        UnitPrice = unitPrice;
        SupplierId = supplierId;
        CurrentStock = 0;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Receives a delivery: adds a <see cref="StockBatch"/> carrying <i>this</i> lot's expiry and batch number,
    /// and raises the on-hand total. Returns the new batch so the caller can report it.
    /// </summary>
    public StockBatch AddStock(int quantity, DateTime? expiryDate = null, string? batchNumber = null)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));

        var batch = new StockBatch(Guid.NewGuid(), Id, quantity, expiryDate, batchNumber);
        _batches.Add(batch);

        CurrentStock += quantity;
        UpdatedAt = DateTime.UtcNow;
        return batch;
    }

    /// <summary>
    /// Attaches a lot that already exists — the migration's opening batch, whose quantity is <b>already</b>
    /// counted in <see cref="CurrentStock"/>. Deliberately does not touch the total: describing stock that is
    /// already on the books must not double it.
    /// </summary>
    public void AttachExistingBatch(StockBatch batch)
    {
        if (batch == null)
            throw new ArgumentNullException(nameof(batch));

        _batches.Add(batch);
    }

    public void RemoveStock(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));

        if (CurrentStock < quantity)
            throw new InvalidOperationException("Insufficient stock");

        ConsumeStock(quantity);
    }

    /// <summary>
    /// Draws <paramref name="quantity"/> down <b>oldest-expiry-first</b> (FEFO — AC-P4.4) and returns the
    /// shortfall no batch could cover.
    ///
    /// FEFO rather than FIFO because expiry is what makes stock unusable: taking from the lot that expires
    /// soonest is what keeps the <i>displayed</i> expiry the one that matters. Undated lots sort last (they can
    /// wait), then oldest-received first.
    ///
    /// Unlike <see cref="RemoveStock"/> this does <b>not</b> throw on a shortfall. AC-P4.12: recording a visit
    /// is never blocked by a stock discrepancy — the clinical work has already happened — so on-hand is allowed
    /// to go negative and the shortfall is surfaced, rather than clamped to zero and lost.
    /// </summary>
    public int ConsumeStock(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));

        var outstanding = quantity;
        foreach (var batch in BatchesInConsumptionOrder())
        {
            if (outstanding == 0)
                break;

            outstanding -= batch.Draw(outstanding);
        }

        CurrentStock -= quantity;
        UpdatedAt = DateTime.UtcNow;
        return outstanding;
    }

    /// <summary>FEFO order: soonest expiry first, undated lots last, then oldest received first.</summary>
    public IEnumerable<StockBatch> BatchesInConsumptionOrder() =>
        _batches
            .Where(b => b.RemainingQuantity > 0)
            .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(b => b.ReceivedAt);

    /// <summary>
    /// The expiry the UI shows (AC-P4.3/4.5): the soonest one still holding stock — the batch that is actually
    /// expiring, not the last one entered. Null when nothing on the shelf carries a date.
    /// </summary>
    public DateTime? EarliestRelevantExpiry()
    {
        DateTime? earliest = null;
        foreach (var batch in _batches)
        {
            if (batch.RemainingQuantity <= 0 || !batch.ExpiryDate.HasValue)
                continue;
            if (earliest == null || batch.ExpiryDate.Value < earliest.Value)
                earliest = batch.ExpiryDate.Value;
        }

        return earliest;
    }

    /// <summary>True when a lot still on the shelf is at or past its expiry date.</summary>
    public bool HasExpiredStock(DateTime asOfUtc) => _batches.Any(b => b.IsExpired(asOfUtc));

    /// <summary>True when a lot still on the shelf expires within <paramref name="leadDays"/>.</summary>
    public bool HasStockExpiringSoon(DateTime asOfUtc, int leadDays) =>
        _batches.Any(b => b.IsExpiringSoon(asOfUtc, leadDays));

    public void UpdateStockLevels(int minimumStockLevel, int maximumStockLevel)
    {
        if (minimumStockLevel < 0)
            throw new ArgumentException("Minimum stock level cannot be negative", nameof(minimumStockLevel));
        if (maximumStockLevel < minimumStockLevel)
            throw new ArgumentException("Maximum stock level must be greater than or equal to minimum", nameof(maximumStockLevel));

        MinimumStockLevel = minimumStockLevel;
        MaximumStockLevel = maximumStockLevel;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsLowStock()
    {
        return CurrentStock <= MinimumStockLevel;
    }

    public bool IsOutOfStock()
    {
        return CurrentStock == 0;
    }

    public void UpdateInfo(
        string name, string? description, string category, string unit, decimal? unitPrice, Guid? supplierId)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        Category = RequireCategory(category);
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        UnitPrice = unitPrice;
        SupplierId = supplierId;
        UpdatedAt = DateTime.UtcNow;
    }

    // The category is required, so Normalize's « null for blank » is a refusal here rather than an unfiled row —
    // unlike ProcedureType.Category, which is genuinely optional.
    private static string RequireCategory(string category)
    {
        if (category == null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        return StockCategories.Normalize(category)
               ?? throw new ArgumentException("La catégorie est requise.", nameof(category));
    }

    /// <summary>
    /// Sets on-hand to an absolute figure — a manual stock-take correction — and returns the <b>signed
    /// delta</b>, so the caller can write the <see cref="StockMovement"/> AC-P4.15 requires. It used to return
    /// nothing and its only caller wrote no movement at all, which is exactly what made Σ movements stop
    /// reconciling with on-hand.
    ///
    /// Batches are reconciled to the new total (a decrease draws down FEFO, an increase becomes an undated
    /// correction lot); without that the batch rows and <see cref="CurrentStock"/> would disagree the first time
    /// a stock-take ran.
    /// </summary>
    public int SetCurrentStock(int quantity)
    {
        if (quantity < 0)
            throw new ArgumentException("Stock quantity cannot be negative", nameof(quantity));

        var delta = quantity - CurrentStock;
        if (delta == 0)
        {
            return 0;
        }

        if (delta < 0)
        {
            ConsumeStock(-delta);
        }
        else
        {
            AddStock(delta);
        }

        return delta;
    }
}
