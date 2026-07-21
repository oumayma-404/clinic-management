namespace ClinicManagement.API.Models;

/// <summary>
/// Request bodies for the CNAM nomenclature admin endpoints. Kept separate from the MediatR commands so the
/// public HTTP contract does not couple to internal command shapes (and route-bound ids like <c>Id</c> are
/// never accepted from the body).
/// </summary>
public class CreateCnamEntryRequest
{
    public string CodeActe { get; set; } = string.Empty;
    public string DesignationFr { get; set; } = string.Empty;
    public string LettreCle { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class UpdateCnamEntryRequest
{
    public string CodeActe { get; set; } = string.Empty;
    public string DesignationFr { get; set; } = string.Empty;
    public string LettreCle { get; set; } = string.Empty;
    public decimal Coefficient { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class UpdateCnamLetterValueRequest
{
    public decimal Value { get; set; }
}
