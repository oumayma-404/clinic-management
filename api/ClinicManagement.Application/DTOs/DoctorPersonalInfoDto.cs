namespace ClinicManagement.Application.DTOs;

/// <summary>
/// DTO for doctor personal information during registration
/// </summary>
public class DoctorPersonalInfoDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string? Phone { get; set; }
}



