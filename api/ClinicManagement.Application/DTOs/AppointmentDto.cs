namespace ClinicManagement.Application.DTOs;

public class AppointmentDto
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid? PatientId { get; set; }
    public string? PatientName { get; set; }
    public Guid? DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public DateTime AppointmentDateTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The statuses this appointment may legally move to right now, from the domain's declared transition table
    /// (AC-P1.6). The status control offers exactly these, and « Annuler le rendez-vous » derives its
    /// <c>disabled</c> state from whether <c>Cancelled</c> is in the list — instead of the client re-deriving a
    /// second copy of the rules that could disagree with the server (and did: the button was disabled on a
    /// completed appointment, which is now a legal cancellation).
    /// </summary>
    public List<string> AllowedNextStatuses { get; set; } = new();
    public Guid? ProcedureTypeId { get; set; }
    public string? ProcedureTypeName { get; set; }
    public string? ProcedureColorHex { get; set; }
    /// <summary>The treatment-plan step this appointment schedules, if any.</summary>
    public Guid? TreatmentPlanItemId { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (PostgreSQL <c>xmin</c>). Send it back on the matching update command so
    /// the save is checked against the copy the user actually edited; a peer's change in between then yields
    /// a 409 instead of a silent overwrite.
    /// </summary>
    public uint Version { get; set; }

    /// <summary>
    /// True when this appointment is reflected in Google Calendar (derived from
    /// <c>GoogleCalendarEventId != null</c>). Drives the "non synchronisé" badge + manual push in
    /// Local offline UX (US-3). Additive — Cloud consumers ignore it.
    /// </summary>
    public bool IsSyncedToGoogle { get; set; }
}
