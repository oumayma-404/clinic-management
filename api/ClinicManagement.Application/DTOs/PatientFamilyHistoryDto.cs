namespace ClinicManagement.Application.DTOs;

public class PatientFamilyHistoryDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string Relationship { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}




