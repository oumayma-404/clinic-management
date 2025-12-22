using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class RecurringAppointment : Entity<Guid>
{
    public Guid PatientId { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }
    public TimeSpan Duration { get; private set; }
    public string RecurrencePattern { get; private set; } // e.g., "Daily", "Weekly", "Monthly"
    public int Interval { get; private set; } // e.g., every 2 weeks
    public string? DoctorName { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation property
    public Patient Patient { get; private set; } = null!;

    private RecurringAppointment() { } // For EF Core

    public RecurringAppointment(
        Guid id,
        Guid patientId,
        DateTime startDate,
        TimeSpan duration,
        string recurrencePattern,
        int interval = 1,
        DateTime? endDate = null,
        string? doctorName = null,
        string? notes = null)
    {
        Id = id;
        PatientId = patientId;
        StartDate = startDate;
        EndDate = endDate;
        Duration = duration;
        RecurrencePattern = recurrencePattern ?? throw new ArgumentNullException(nameof(recurrencePattern));
        Interval = interval;
        DoctorName = doctorName;
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



