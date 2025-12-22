namespace ClinicManagement.Application.DTOs;

public class AppointmentDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public DateTime AppointmentDateTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? DoctorName { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}



