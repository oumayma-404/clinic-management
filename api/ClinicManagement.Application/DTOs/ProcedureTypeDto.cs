namespace ClinicManagement.Application.DTOs;

public class ProcedureTypeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DefaultDurationMinutes { get; set; }
    public decimal? DefaultCost { get; set; }
    public string ColorHex { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Resulting odontogram state (ToothCondition name) a dental act of this procedure produces; null = none.</summary>
    public string? ResultingCondition { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}


