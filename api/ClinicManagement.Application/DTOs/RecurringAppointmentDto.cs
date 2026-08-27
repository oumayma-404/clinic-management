using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.DTOs;

public class RecurringAppointmentDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public Guid? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public Guid? ProcedureTypeId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? OccurrenceCount { get; set; }
    public string RecurrencePattern { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public int AppointmentCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>The outcome of creating a recurring series: how many occurrences were generated, skipped as past,
/// or skipped as conflicting with an existing appointment.</summary>
public class RecurringSeriesResultDto
{
    public Guid RecurringAppointmentId { get; set; }
    public int CreatedCount { get; set; }
    public int SkippedPastCount { get; set; }
    public List<DateTime> Conflicts { get; set; } = new();

    /// <summary>
    /// Occurrences skipped because they fell outside the practitioner's working hours (AC-P1.28/1.36).
    /// Reported separately from <see cref="Conflicts"/> so the UI can offer the right remedy: a conflict needs
    /// a different slot, an out-of-hours date needs either different hours or a confirmed override.
    /// </summary>
    public List<DateTime> OutsideWorkingHours { get; set; } = new();
}

public static class RecurringAppointmentMappingExtensions
{
    public static RecurringAppointmentDto ToDto(this RecurringAppointment r, string? patientName = null, int appointmentCount = 0) => new()
    {
        Id = r.Id,
        ClinicId = r.ClinicId,
        PatientId = r.PatientId,
        PatientName = patientName ?? r.Patient?.GetFullName(),
        DoctorId = r.DoctorId,
        DoctorName = r.DoctorName,
        ProcedureTypeId = r.ProcedureTypeId,
        StartDate = r.StartDate,
        EndDate = r.EndDate,
        OccurrenceCount = r.OccurrenceCount,
        RecurrencePattern = r.RecurrencePattern,
        Interval = r.Interval,
        Notes = r.Notes,
        IsActive = r.IsActive,
        AppointmentCount = appointmentCount,
        CreatedAt = r.CreatedAt
    };
}
