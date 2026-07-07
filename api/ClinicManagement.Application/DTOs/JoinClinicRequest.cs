namespace ClinicManagement.Application.DTOs;

public class JoinClinicRequest
{
    public string Code { get; set; } = string.Empty;
    public string Role { get; set; } = "secretary"; // "doctor" or "secretary"
    public DoctorPersonalInfoDto? DoctorInfo { get; set; } // Required if Role is "doctor"
}


