using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class StockItem : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string Category { get; private set; } // e.g., "Medicine", "Consumable"
    public string Unit { get; private set; } // e.g., "Box", "Bottle", "Unit"
    public int CurrentStock { get; private set; }
    public int MinimumStockLevel { get; private set; }
    public int MaximumStockLevel { get; private set; }
    public decimal? UnitPrice { get; private set; }
    public string? Supplier { get; private set; }
    public DateTime? ExpiryDate { get; private set; }
    public string? BatchNumber { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

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
        string? supplier = null)
    {
        Id = id;
        ClinicId = clinicId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        MinimumStockLevel = minimumStockLevel;
        MaximumStockLevel = maximumStockLevel;
        Description = description;
        UnitPrice = unitPrice;
        Supplier = supplier;
        CurrentStock = 0;
        CreatedAt = DateTime.UtcNow;
    }

    public void AddStock(int quantity, DateTime? expiryDate = null, string? batchNumber = null)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));

        CurrentStock += quantity;
        if (expiryDate.HasValue)
            ExpiryDate = expiryDate;
        if (!string.IsNullOrWhiteSpace(batchNumber))
            BatchNumber = batchNumber;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveStock(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero", nameof(quantity));

        if (CurrentStock < quantity)
            throw new InvalidOperationException("Insufficient stock");

        CurrentStock -= quantity;
        UpdatedAt = DateTime.UtcNow;
    }

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

    public void UpdateInfo(string name, string? description, string category, string unit, decimal? unitPrice, string? supplier)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        Category = category ?? throw new ArgumentNullException(nameof(category));
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        UnitPrice = unitPrice;
        Supplier = supplier;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetCurrentStock(int quantity)
    {
        if (quantity < 0)
            throw new ArgumentException("Stock quantity cannot be negative", nameof(quantity));

        CurrentStock = quantity;
        UpdatedAt = DateTime.UtcNow;
    }
}



