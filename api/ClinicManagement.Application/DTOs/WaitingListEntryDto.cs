using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

public class WaitingListEntryDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public Guid? PreferredDoctorId { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string? DesiredTimeframe { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? ResultingAppointmentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public static class WaitingListEntryMappingExtensions
{
    public static WaitingListEntryDto ToDto(this WaitingListEntry entry, string? patientName = null) => new()
    {
        Id = entry.Id,
        ClinicId = entry.ClinicId,
        PatientId = entry.PatientId,
        PatientName = patientName ?? entry.Patient?.GetFullName(),
        PreferredDoctorId = entry.PreferredDoctorId,
        Priority = entry.Priority.ToString(),
        DesiredTimeframe = entry.DesiredTimeframe,
        Note = entry.Note,
        Status = entry.Status.ToString(),
        ResultingAppointmentId = entry.ResultingAppointmentId,
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt
    };
}
