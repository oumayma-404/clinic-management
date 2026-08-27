namespace ClinicManagement.Application.DTOs;

/// <summary>One stock movement (sortie/entrée) in an item's audit history.</summary>
public class StockMovementDto
{
    public Guid Id { get; set; }
    public Guid StockItemId { get; set; }
    /// <summary>Consume | Restock</summary>
    public string Type { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int ResultingStock { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
