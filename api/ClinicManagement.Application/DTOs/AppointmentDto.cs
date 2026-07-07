namespace ClinicManagement.Application.DTOs;

public class AppointmentDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid? PatientId { get; set; }
    public string? PatientName { get; set; }
    public string? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public DateTime AppointmentDateTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? ProcedureTypeId { get; set; }
    public string? ProcedureTypeName { get; set; }
    public string? ProcedureColorHex { get; set; }
    public DateTime CreatedAt { get; set; }
}
