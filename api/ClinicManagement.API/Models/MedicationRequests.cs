namespace ClinicManagement.API.Models;

/// <summary>
/// Request bodies for the medication catalog admin endpoints. Kept separate from the MediatR commands so the
/// public HTTP contract does not couple to internal command shapes (and route-bound ids like <c>Id</c> are
/// never accepted from the body).
/// </summary>
public class CreateMedicationRequest
{
    public string BrandName { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
    public List<string> Dcis { get; set; } = new();
}

public class UpdateMedicationRequest
{
    public string BrandName { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
    public List<string> Dcis { get; set; } = new();
}
