namespace ClinicManagement.Application.DTOs;

public class PatientMedicalHistoryDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}




