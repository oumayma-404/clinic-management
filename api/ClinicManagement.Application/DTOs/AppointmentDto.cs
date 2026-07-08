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

    /// <summary>
    /// True when this appointment is reflected in Google Calendar (derived from
    /// <c>GoogleCalendarEventId != null</c>). Drives the "non synchronisé" badge + manual push in
    /// Local offline UX (US-3). Additive — Cloud consumers ignore it.
    /// </summary>
    public bool IsSyncedToGoogle { get; set; }
}
