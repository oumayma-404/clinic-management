using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A patient on the clinic waiting list (salle d'attente / liste d'attente) — someone waiting for a slot
/// to free up. Clinic-scoped; carries an optional preferred doctor, a priority and a free-text desired
/// timeframe ("matin", "cette semaine", …). Either promoted to a real appointment (storing the resulting
/// appointment id) or cancelled.
/// </summary>
public class WaitingListEntry : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid PatientId { get; private set; }
    public Guid? PreferredDoctorId { get; private set; }
    public WaitingListPriority Priority { get; private set; }
    public string? DesiredTimeframe { get; private set; }
    public string? Note { get; private set; }
    public WaitingListStatus Status { get; private set; }
    public Guid? ResultingAppointmentId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation properties
    public Patient Patient { get; private set; } = null!;

    private WaitingListEntry() { } // For EF Core

    public WaitingListEntry(
        Guid id,
        Guid clinicId,
        Guid patientId,
        WaitingListPriority priority,
        Guid? preferredDoctorId = null,
        string? desiredTimeframe = null,
        string? note = null)
    {
        Id = id;
        ClinicId = clinicId;
        PatientId = patientId;
        Priority = priority;
        PreferredDoctorId = preferredDoctorId;
        DesiredTimeframe = desiredTimeframe;
        Note = note;
        Status = WaitingListStatus.Waiting;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(WaitingListPriority priority, Guid? preferredDoctorId, string? desiredTimeframe, string? note)
    {
        Priority = priority;
        PreferredDoctorId = preferredDoctorId;
        DesiredTimeframe = desiredTimeframe;
        Note = note;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Promote(Guid? resultingAppointmentId)
    {
        if (Status != WaitingListStatus.Waiting)
            throw new InvalidOperationException("Seule une entrée en attente peut être convertie en rendez-vous.");

        Status = WaitingListStatus.Promoted;
        ResultingAppointmentId = resultingAppointmentId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status == WaitingListStatus.Promoted)
            throw new InvalidOperationException("Une entrée déjà convertie en rendez-vous ne peut pas être annulée.");

        Status = WaitingListStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }
}
