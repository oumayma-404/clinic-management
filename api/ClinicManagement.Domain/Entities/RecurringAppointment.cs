using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A recurring-appointment series template (clinical-workflow-depth). Defines the recurrence (pattern +
/// interval + an end condition: an end date and/or a fixed occurrence count) that expands into individual
/// <see cref="Appointment"/> rows linked back via <c>Appointment.RecurringAppointmentId</c>. Clinic-scoped.
/// </summary>
public class RecurringAppointment : Entity<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid? DoctorId { get; private set; }
    public Guid? ProcedureTypeId { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }
    /// <summary>End condition: a fixed number of occurrences (null when the series ends by date only).</summary>
    public int? OccurrenceCount { get; private set; }
    public TimeSpan Duration { get; private set; }
    public string RecurrencePattern { get; private set; } // RecurrenceFrequency name: "Daily"/"Weekly"/"Monthly"
    public int Interval { get; private set; } // e.g. every 2 weeks
    public string? DoctorName { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation property
    public Patient Patient { get; private set; } = null!;

    private RecurringAppointment() { } // For EF Core

    public RecurringAppointment(
        Guid id,
        Guid clinicId,
        Guid patientId,
        DateTime startDate,
        TimeSpan duration,
        string recurrencePattern,
        int interval = 1,
        DateTime? endDate = null,
        int? occurrenceCount = null,
        Guid? doctorId = null,
        string? doctorName = null,
        Guid? procedureTypeId = null,
        string? notes = null)
    {
        Id = id;
        ClinicId = clinicId;
        PatientId = patientId;
        StartDate = startDate;
        Duration = duration;
        RecurrencePattern = recurrencePattern ?? throw new ArgumentNullException(nameof(recurrencePattern));
        Interval = interval < 1 ? 1 : interval;
        EndDate = endDate;
        OccurrenceCount = occurrenceCount;
        DoctorId = doctorId;
        DoctorName = doctorName;
        ProcedureTypeId = procedureTypeId;
        Notes = notes;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void UpdateEndDate(DateTime? endDate)
    {
        EndDate = endDate;
    }
}
