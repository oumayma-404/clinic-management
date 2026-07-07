namespace ClinicManagement.Application.DTOs;

public class PatientFlagDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string FlagType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}




