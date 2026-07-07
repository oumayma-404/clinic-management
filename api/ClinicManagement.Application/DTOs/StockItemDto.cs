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
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public static class StockItemMappingExtensions
{
    public static StockItemDto ToDto(this StockItem item) => new()
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
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };
}
