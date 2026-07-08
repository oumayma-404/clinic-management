using ClinicManagement.Application.DTOs;

namespace ClinicManagement.API.Models;

/// <summary>Staff self-registration payload (Local mode): join a clinic by code with credentials.</summary>
public class RegisterRequest
{
    public string Code { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "secretary"; // "doctor" or "secretary"
    public DoctorPersonalInfoDto? DoctorInfo { get; set; }
}
