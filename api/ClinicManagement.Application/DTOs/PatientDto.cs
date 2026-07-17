namespace ClinicManagement.Application.DTOs;

public class PatientDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? MedicalHistory { get; set; }
    public string? Allergies { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public AddressDto? Address { get; set; }
    public InsuranceInfoDto? InsuranceInfo { get; set; }
    public CnamInfoDto? CnamInfo { get; set; }
    public List<PatientFlagDto> Flags { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
