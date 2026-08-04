namespace ClinicManagement.Application.DTOs;

public class ProcedureTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DefaultDurationMinutes { get; set; }
    public decimal? DefaultCost { get; set; }
    public string ColorHex { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>
    /// Clinical discipline (« Endodontie », « Prothèse fixe »); null = unfiled, which the UI groups last under
    /// « Sans catégorie ». Open text canonicalised on write — see <c>ProcedureTypeCategories</c>.
    /// </summary>
    public string? Category { get; set; }
    /// <summary>Resulting odontogram state (ToothCondition name) a dental act of this procedure produces; null = none.</summary>
    public string? ResultingCondition { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// The act's material list (AC-P4.9/4.14) — the stock performing it consumes. Empty for an act that has
    /// opted out, which is the default and behaves exactly as before (AC-P4.11).
    /// </summary>
    public List<ProcedureTypeMaterialDto> Materials { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>One line of an act's material list: performing the act consumes N of this stock item.</summary>
public class ProcedureTypeMaterialDto
{
    public Guid StockItemId { get; set; }
    public int QuantityPerAct { get; set; }
}

public static class ProcedureTypeMappingExtensions
{
    /// <summary>
    /// The single mapping for a procedure type. It exists because the DTO was hand-built at four call sites
    /// (create, update, get-one, get-many): adding <see cref="ProcedureTypeDto.Materials"/> to three of the
    /// four would have left the list quietly empty on the fourth, and an empty material list is exactly how
    /// an act that has opted out reads (AC-P4.11) — the one failure mode it must never be confusable with.
    /// </summary>
    public static ProcedureTypeDto ToDto(this Domain.Entities.ProcedureType procedureType) =>
        new()
        {
            Id = procedureType.Id,
            Name = procedureType.Name,
            DefaultDurationMinutes = procedureType.DefaultDurationMinutes,
            DefaultCost = procedureType.DefaultCost,
            ColorHex = procedureType.Color.Value,
            Description = procedureType.Description,
            Category = procedureType.Category,
            ResultingCondition = procedureType.ResultingCondition?.ToString(),
            IsActive = procedureType.IsActive,
            Materials = procedureType.Materials
                .Select(m => new ProcedureTypeMaterialDto
                {
                    StockItemId = m.StockItemId,
                    QuantityPerAct = m.QuantityPerAct,
                })
                .ToList(),
            CreatedAt = procedureType.CreatedAt,
            UpdatedAt = procedureType.UpdatedAt,
        };
}
