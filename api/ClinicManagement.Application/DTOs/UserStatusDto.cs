namespace ClinicManagement.Application.DTOs;

public class UserStatusDto
{
    public bool HasClinic { get; set; }
    public Guid? ClinicId { get; set; }
    public string? ClinicName { get; set; }
    public string? Role { get; set; }
    public UserDto? User { get; set; }
    public ClinicDto? Clinic { get; set; }
    public List<DoctorDto>? Doctors { get; set; }
}

