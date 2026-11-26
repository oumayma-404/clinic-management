using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Services;

/// <summary>
/// Who goes next in the salle d'attente (`salle-attente` D3) — the <b>one</b> owner of that order, read by the board
/// and by « Patient suivant » alike, so the name on the doctor's button is the first card at reception.
///
/// <para><b>Nobody goes before the time of his RDV, and a late patient loses his place.</b> A booked patient is
/// ready at the later of his slot and his arrival; a walk-in is ready when he walks in. Ties go to whoever arrived
/// first, then to the id, so two screens never order one tie two ways.</para>
///
/// <para>⚠️ The browser never re-sorts: it shows the order this returns. A second copy of the rule client-side is the
/// shape of defect this repo keeps paying for — the two agree until the day one of them is changed.</para>
/// </summary>
public static class WaitingRoomOrder
{
    /// <summary>The instant the patient is ready to be seen.</summary>
    public static DateTime ReadyAt(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        var arrived = appointment.ArrivedAtUtc ?? appointment.AppointmentDateTime;
        return appointment.IsWalkIn || arrived > appointment.AppointmentDateTime
            ? arrived
            : appointment.AppointmentDateTime;
    }

    /// <summary>
    /// The waiting patients in the order they will be called. « Passer en premier » (round 2, E2) goes before
    /// everything else, the earliest-marked first — a toothache that walked in is seen before the 10:30 who came
    /// early, and two of them are seen in the order reception said so.
    /// </summary>
    public static IReadOnlyList<Appointment> Order(IEnumerable<Appointment> waiting) => waiting
        .OrderBy(a => a.UrgentAtUtc is null ? 1 : 0)
        .ThenBy(a => a.UrgentAtUtc ?? DateTime.MaxValue)
        .ThenBy(ReadyAt)
        .ThenBy(a => a.ArrivedAtUtc ?? a.AppointmentDateTime)
        .ThenBy(a => a.Id)
        .ToList();
}
