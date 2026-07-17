using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Events;

namespace ClinicManagement.Domain.Entities;

public class Appointment : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid? PatientId { get; private set; }
    public string? DoctorId { get; private set; }
    public DateTime AppointmentDateTime { get; private set; }
    public TimeSpan Duration { get; private set; }
    public string? DoctorName { get; private set; }
    public string? Notes { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? RecurringAppointmentId { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? GoogleCalendarEventId { get; private set; }
    public Guid? ProcedureTypeId { get; private set; }
    public int? ProcedureDurationMinutes { get; private set; }
    public string? ProcedureColorHex { get; private set; }

    // Navigation properties
    public Clinic Clinic { get; private set; } = null!;
    public Patient? Patient { get; private set; }
    public ProcedureType? ProcedureType { get; private set; }

    private Appointment() { } // For EF Core

    public Appointment(
        Guid id,
        Guid clinicId,
        Guid? patientId,
        string? doctorId,
        DateTime appointmentDateTime,
        TimeSpan duration,
        string? doctorName = null,
        string? notes = null,
        Guid? recurringAppointmentId = null,
        Guid? procedureTypeId = null,
        int? procedureDurationMinutes = null,
        string? procedureColorHex = null)
    {
        Id = id;
        ClinicId = clinicId;
        PatientId = patientId;
        DoctorId = doctorId;
        AppointmentDateTime = appointmentDateTime;
        Duration = duration;
        DoctorName = doctorName;
        Notes = notes;
        Status = AppointmentStatus.Scheduled;
        RecurringAppointmentId = recurringAppointmentId;
        ProcedureTypeId = procedureTypeId;
        ProcedureDurationMinutes = procedureDurationMinutes;
        ProcedureColorHex = procedureColorHex;
        CreatedAt = DateTime.UtcNow;

        if (patientId.HasValue)
        {
            AddDomainEvent(new AppointmentCreatedEvent(id, patientId.Value, appointmentDateTime));
        }
    }

    public void Confirm()
    {
        if (Status == AppointmentStatus.Cancelled)
            throw new InvalidOperationException("Cannot confirm a cancelled appointment");

        Status = AppointmentStatus.Confirmed;
        UpdatedAt = DateTime.UtcNow;
        if (PatientId.HasValue)
        {
            AddDomainEvent(new AppointmentConfirmedEvent(Id, PatientId.Value, AppointmentDateTime));
        }
    }

    public void Start()
    {
        if (Status != AppointmentStatus.Confirmed && Status != AppointmentStatus.Scheduled)
            throw new InvalidOperationException("Appointment must be confirmed or scheduled to start");

        Status = AppointmentStatus.InProgress;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Complete()
    {
        if (Status != AppointmentStatus.InProgress)
            throw new InvalidOperationException("Appointment must be in progress to complete");

        Status = AppointmentStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records that the visit happened (a medical record was filled for it). Allowed from
    /// <see cref="AppointmentStatus.Scheduled"/>/<see cref="AppointmentStatus.Confirmed"/>/<see cref="AppointmentStatus.InProgress"/>;
    /// a Cancelled/Completed/NoShow appointment is left unchanged (idempotent no-op, so a second staff
    /// member filling a record is harmless — spec AC-7).
    /// </summary>
    public void MarkVisitCompleted()
    {
        if (Status != AppointmentStatus.Scheduled &&
            Status != AppointmentStatus.Confirmed &&
            Status != AppointmentStatus.InProgress)
        {
            return;
        }

        Status = AppointmentStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel(string? reason = null)
    {
        if (Status == AppointmentStatus.Completed)
            throw new InvalidOperationException("Cannot cancel a completed appointment");

        Status = AppointmentStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsNoShow()
    {
        if (Status == AppointmentStatus.Completed || Status == AppointmentStatus.Cancelled)
            throw new InvalidOperationException("Cannot mark completed or cancelled appointment as no show");

        Status = AppointmentStatus.NoShow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reschedule(DateTime newDateTime)
    {
        if (Status == AppointmentStatus.Completed)
            throw new InvalidOperationException("Cannot reschedule a completed appointment");

        if (Status == AppointmentStatus.Cancelled)
            throw new InvalidOperationException("Cannot reschedule a cancelled appointment");

        var oldDateTime = AppointmentDateTime;
        AppointmentDateTime = newDateTime;
        Status = AppointmentStatus.Scheduled;
        UpdatedAt = DateTime.UtcNow;
        if (PatientId.HasValue)
        {
            AddDomainEvent(new AppointmentRescheduledEvent(Id, PatientId.Value, oldDateTime, newDateTime));
        }
    }

    public void UpdateNotes(string? notes)
    {
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDoctorName(string? doctorName)
    {
        DoctorName = doctorName;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentException("Duration must be greater than zero", nameof(duration));

        Duration = duration;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetGoogleCalendarEventId(string? eventId)
    {
        GoogleCalendarEventId = eventId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetProcedureType(Guid? procedureTypeId, int? procedureDurationMinutes, string? procedureColorHex)
    {
        ProcedureTypeId = procedureTypeId;
        ProcedureDurationMinutes = procedureDurationMinutes;
        ProcedureColorHex = procedureColorHex;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsUpcoming()
    {
        return AppointmentDateTime > DateTime.UtcNow &&
               (Status == AppointmentStatus.Scheduled || Status == AppointmentStatus.Confirmed);
    }
}

