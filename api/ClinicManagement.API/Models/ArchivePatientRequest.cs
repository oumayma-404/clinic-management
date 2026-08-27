namespace ClinicManagement.API.Models;

/// <summary>Body of <c>POST /api/patients/{id}/archive</c>. The reason is optional and stored for context.</summary>
public class ArchivePatientRequest
{
    public string? Reason { get; set; }
}
