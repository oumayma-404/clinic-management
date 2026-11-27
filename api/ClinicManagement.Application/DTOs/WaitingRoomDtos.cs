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

    /// <summary>
    /// « Attente moyenne » (round 2, Q2): arrival → called in, in whole minutes, over today's patients already
    /// called in on this board (its dentist filter included). Null when nobody has been called in with both times
    /// known. It moves only when somebody goes in — never with the clock, never back to « — » when the room empties.
    /// </summary>
    public int? AverageWaitMinutes { get; set; }

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

    /// <summary>On congé today: offered by no « quel praticien ? », and his cards say so (round 2, C10).</summary>
    public bool AbsentToday { get; set; }

    /// <summary>« 2 fauteuils » (round 2, Q3): « Patient suivant » ends nobody, two patients may be seated.</summary>
    public bool WorksTwoChairs { get; set; }
}

/// <summary>One visit on the board.</summary>
public class WaitingRoomVisitDto
{
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;

    /// <summary>The patient's number as recorded — « Qui arrive ? » finds today's RDV by phone too (round 2, C5).</summary>
    public string? PatientPhone { get; set; }

    public Guid? DoctorId { get; set; }
    public string? DoctorName { get; set; }

    /// <summary>
    /// Why this visit's dentist cannot see him today (round 2, C10): <c>OnLeave</c> · <c>NotInTeam</c> (retired, or
    /// no longer a practitioner of this cabinet). Null when the dentist is here or none is named.
    /// </summary>
    public string? DoctorUnavailable { get; set; }

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

    /// <summary>« Passer en premier » (round 2, E2): set while this patient goes before the queue.</summary>
    public DateTime? UrgentAtUtc { get; set; }

    /// <summary>« Pas là » (round 2, E1): when he was called and was not there; cleared at his next call.</summary>
    public DateTime? CallSkippedAtUtc { get; set; }

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

    /// <summary>
    /// Every visit it touched as it was <b>after</b> (round 2, C9). « Annuler » sends them back, and the server
    /// refuses the undo when a card has moved since — a colleague's move, a payment, a fiche — instead of silently
    /// overwriting it.
    /// </summary>
    public List<WaitingRoomSnapshotDto> After { get; set; } = new();

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

/// <summary>
/// A visit's board state at one instant — the unit an « Annuler » restores. Everything a board action can change,
/// the RDV's own time included: the RDV takes the real chair time when the visit ends (Q1), and « Le recevoir
/// aujourd'hui » moves a RDV from another day (C6).
/// </summary>
public class WaitingRoomSnapshotDto
{
    public Guid AppointmentId { get; set; }
    public string Lane { get; set; } = string.Empty;
    public DateTime? ArrivedAtUtc { get; set; }
    public DateTime? CalledInAtUtc { get; set; }
    public DateTime? LeftChairAtUtc { get; set; }
    public DateTime? LeftUnseenAtUtc { get; set; }
    public DateTime? UrgentAtUtc { get; set; }
    public DateTime? CallSkippedAtUtc { get; set; }
    public Guid? DoctorId { get; set; }
    public DateTime AppointmentDateTime { get; set; }
    public int DurationMinutes { get; set; }
    public DateTime? PlannedStartUtc { get; set; }
    public int? PlannedDurationMinutes { get; set; }
    public bool BookedWithOverlap { get; set; }
}

/// <summary>A patient's next RDV on another day — « Qui arrive ? » offers « Le recevoir aujourd'hui » (round 2, C6).</summary>
public class WaitingRoomOtherDayVisitDto
{
    public Guid PatientId { get; set; }
    public Guid AppointmentId { get; set; }
    public DateTime AppointmentDateTime { get; set; }
    public string? DoctorName { get; set; }
    public string? Acts { get; set; }
}
