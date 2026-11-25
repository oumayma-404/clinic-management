namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Where a visit of the day sits on the « Salle d'attente » board (`salle-attente`). <b>Derived, never stored</b> —
/// see <c>Appointment.WaitingRoomLane</c>: a stored copy would be a second answer able to disagree with the status
/// and the three presence times it is read from.
/// </summary>
public enum WaitingRoomLane
{
    /// <summary>Not on the board: no patient (« créneau occupé »), retired from the figures, absent, or cancelled from the agenda.</summary>
    None = 0,

    /// <summary>« À venir » — booked today, not here yet.</summary>
    Upcoming = 1,

    /// <summary>« En salle d'attente » — here, not called in yet.</summary>
    Waiting = 2,

    /// <summary>« Au fauteuil » — called in, not left yet.</summary>
    InChair = 3,

    /// <summary>« Terminé » — the visit is over.</summary>
    Done = 4,

    /// <summary>« Parti sans être vu » — came, then left before being called. Shown under « Terminé », in red.</summary>
    Left = 5,
}
