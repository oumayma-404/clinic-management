namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The « Salle d'attente » board for today (`salle-attente`): four lanes, already in their order, and who may call
/// the next patient. <see cref="ServerNowUtc"/> is what every wait timer counts from — a cabinet PC's clock drifts,
/// so the browser offsets its own clock by the difference instead of trusting it.
/// </summary>
public class WaitingRoomDto
{
    public DateTime ServerNowUtc { get; set; }

    /// <summary>The cabinet's active dentists. Two or more is what makes the board ask « quel praticien ? ».</summary>
    public List<WaitingRoomDentistDto> Dentists { get; set; } = new();

    /// <summary>The caller's own practitioner record, when the caller is one — the chair « Patient suivant » serves.</summary>
    public Guid? CallerDoctorId { get; set; }

    /// <summary>True for a dentist (or an admin standing for the only one): the « Patient suivant » button is theirs.</summary>
    public bool CanCallNext { get; set; }

    public List<WaitingRoomVisitDto> Upcoming { get; set; } = new();

    /// <summary>In the order they will be called (<c>WaitingRoomOrder</c>) — never re-sorted by the browser.</summary>
    public List<WaitingRoomVisitDto> Waiting { get; set; } = new();

    /// <summary>One per chair — one per dentist in a cabinet de groupe.</summary>
    public List<WaitingRoomVisitDto> InChair { get; set; } = new();

    /// <summary>Finished and « parti sans être vu », most recent first.</summary>
    public List<WaitingRoomVisitDto> Done { get; set; } = new();
}

public class WaitingRoomDentistDto
{
    public Guid DoctorId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>One visit on the board.</summary>
public class WaitingRoomVisitDto
{
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public Guid? DoctorId { get; set; }
    public string? DoctorName { get; set; }

    /// <summary>The chair this visit belongs to: its dentist, or the only one when it names none.</summary>
    public Guid? ChairDoctorId { get; set; }

    public DateTime AppointmentDateTime { get; set; }
    public int DurationMinutes { get; set; }

    /// <summary><c>Upcoming</c> · <c>Waiting</c> · <c>InChair</c> · <c>Done</c> · <c>Left</c>.</summary>
    public string Lane { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
    public bool IsWalkIn { get; set; }
    public DateTime? ArrivedAtUtc { get; set; }
    public DateTime? CalledInAtUtc { get; set; }
    public DateTime? LeftChairAtUtc { get; set; }

    /// <summary>When a « parti sans être vu » left — the board shows it in place of a chair time.</summary>
    public DateTime? LeftAtUtc { get; set; }

    /// <summary>The acts booked, joined — « Détartrage + Composite ». Null when none was.</summary>
    public string? Acts { get; set; }

    /// <summary>1-based place in the queue, waiting lane only.</summary>
    public int? Position { get; set; }
}

/// <summary>
/// What a board action did, in the shape its « Annuler » needs: every visit it touched as it was <b>before</b>, and
/// any walk-in it created. Posted back unchanged to <c>POST /api/appointments/waiting-room/restore</c>.
/// </summary>
public class WaitingRoomChangeDto
{
    /// <summary>False when the visit was already where it was asked to go — the screen then shows no toast.</summary>
    public bool Changed { get; set; }

    public List<WaitingRoomSnapshotDto> Before { get; set; } = new();

    /// <summary>Walk-ins this action created; undoing it takes them off the agenda and the board.</summary>
    public List<Guid> RemoveWalkInIds { get; set; } = new();

    /// <summary>The visit the action was about — the one moved, created, or called in.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>Its patient's name, for the toast (« Amira Ben Salah → Au fauteuil »).</summary>
    public string? PatientName { get; set; }

    /// <summary>The lane it ended in.</summary>
    public string? Lane { get; set; }

    /// <summary>
    /// Set when a « Sans RDV » named a patient who had a RDV today: that RDV was marked arrived instead of booking
    /// a second visit beside it.
    /// </summary>
    public bool UsedTodaysAppointment { get; set; }
}

/// <summary>A visit's board state at one instant — the unit an « Annuler » restores.</summary>
public class WaitingRoomSnapshotDto
{
    public Guid AppointmentId { get; set; }
    public string Lane { get; set; } = string.Empty;
    public DateTime? ArrivedAtUtc { get; set; }
    public DateTime? CalledInAtUtc { get; set; }
    public DateTime? LeftChairAtUtc { get; set; }
    public Guid? DoctorId { get; set; }
}
