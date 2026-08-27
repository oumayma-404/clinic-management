using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Common.Services;

/// <summary>Which level supplied the hours in force.</summary>
public enum WorkingHoursSource
{
    /// <summary>Nothing configured anywhere — booking is <b>unrestricted</b> (AC-P1.30).</summary>
    None = 0,

    /// <summary>The clinic-wide hours.</summary>
    Clinic = 1,

    /// <summary>This practitioner's own override.</summary>
    Doctor = 2,
}

/// <summary>
/// The hours in force for one practitioner, plus whether anything stored was unreadable.
/// </summary>
/// <param name="Source">Where <paramref name="Days"/> came from.</param>
/// <param name="Days">The open days. Empty when <paramref name="Source"/> is <see cref="WorkingHoursSource.None"/>.</param>
/// <param name="UnreadableAt">
/// Set when a stored value existed but could not be read (AC-P1.24). Resolution still falls through to the next
/// level, but the caller must surface this rather than let it read as "no restriction".
/// </param>
public sealed record EffectiveWorkingHours(
    WorkingHoursSource Source,
    List<WorkingDayDto> Days,
    WorkingHoursSource? UnreadableAt = null)
{
    /// <summary>True when no hours are configured anywhere, i.e. booking is unrestricted.</summary>
    public bool Unrestricted => Source == WorkingHoursSource.None;
}

/// <summary>
/// The <b>one</b> place effective working hours are resolved and checked (AC-P1.35).
/// <para>
/// There was no resolver anywhere before this — verified by exhaustive search. Nothing in
/// <c>Features/Appointments/</c> read working hours at all, so booking at 03:00 on a Sunday was entirely
/// unconstrained; the doctor→clinic→default fallback existed only as prose in a comment on
/// <c>Doctor.WorkingHoursJson</c> and as two partial copies in frontend components that never consulted the
/// doctor level. The booking guard, the hours editor and the calendar grid all read this type so they cannot
/// disagree about when the clinic is open.
/// </para>
/// <para>
/// Pure and static: no repository, no clock injection. Callers pass the two stored JSON values and the UTC
/// instant, keeping this unit-testable and side-effect free.
/// </para>
/// </summary>
public static class WorkingHoursResolver
{
    /// <summary>
    /// Resolve the hours in force: doctor override → clinic hours → none (AC-P1.30).
    /// <para>
    /// An <b>unreadable</b> value does not stop resolution — it falls through to the next level and is reported
    /// via <see cref="EffectiveWorkingHours.UnreadableAt"/>. Refusing to resolve would take a clinic's booking
    /// offline over a malformed string; pretending it was absent would silently drop enforcement.
    /// </para>
    /// </summary>
    public static EffectiveWorkingHours Resolve(string? doctorJson, string? clinicJson)
    {
        var doctor = WorkingHoursSerializer.Read(doctorJson);
        if (doctor.State == WorkingHoursReadState.Valid)
        {
            return new EffectiveWorkingHours(WorkingHoursSource.Doctor, doctor.Days);
        }

        var unreadableAt = doctor.State == WorkingHoursReadState.Unreadable
            ? WorkingHoursSource.Doctor
            : (WorkingHoursSource?)null;

        var clinic = WorkingHoursSerializer.Read(clinicJson);
        if (clinic.State == WorkingHoursReadState.Valid)
        {
            return new EffectiveWorkingHours(WorkingHoursSource.Clinic, clinic.Days, unreadableAt);
        }

        // Report the doctor-level problem in preference to the clinic-level one: it is the more specific of the
        // two and the one an admin editing that practitioner can act on.
        unreadableAt ??= clinic.State == WorkingHoursReadState.Unreadable ? WorkingHoursSource.Clinic : null;
        return new EffectiveWorkingHours(WorkingHoursSource.None, new List<WorkingDayDto>(), unreadableAt);
    }

    /// <summary>
    /// Is the whole appointment window inside the open hours?
    /// <para>
    /// Returns <c>true</c> when nothing is configured (AC-P1.30) — a clinic that has never opened the settings
    /// screen behaves exactly as before, which is the majority case on day one and the safety valve for
    /// <b>R-12</b> (enforcement must not stop a clinic booking).
    /// </para>
    /// <para>
    /// <paramref name="startUtc"/> is a UTC instant; the comparison happens in <b>clinic-local</b> time via
    /// <see cref="ClinicClock"/>, because the stored hours are wall-clock times. Comparing a UTC hour against
    /// « 09:00 » would be wrong by the offset — an 08:30 UTC booking is 09:30 in Tunis and perfectly legal.
    /// </para>
    /// </summary>
    /// <param name="reason">
    /// A French explanation naming the day and the open window, or the fact the day is closed. Null on success.
    /// </param>
    public static bool IsWithin(
        EffectiveWorkingHours hours,
        DateTime startUtc,
        TimeSpan duration,
        out string? reason)
    {
        reason = null;
        if (hours.Unrestricted)
        {
            return true;
        }

        var localStart = ClinicClock.ToClinicLocal(startUtc);
        var localEnd = localStart + duration;

        // A window crossing midnight can never fit a single day's hours, and treating it as "closed on the
        // start day" would give a misleading reason.
        if (localEnd.Date != localStart.Date)
        {
            reason = "Un rendez-vous ne peut pas se prolonger après minuit.";
            return false;
        }

        var dayName = WorkingHoursSerializer.Weekdays[(int)localStart.DayOfWeek];
        var day = hours.Days.FirstOrDefault(d =>
            string.Equals(d.Day?.Trim(), dayName, StringComparison.OrdinalIgnoreCase));

        if (day == null || !day.Enabled)
        {
            reason = $"Le cabinet est fermé le {WorkingHoursSerializer.FrenchDay(dayName)}.";
            return false;
        }

        if (!WorkingHoursSerializer.TryParseTime(day.From, out var from)
            || !WorkingHoursSerializer.TryParseTime(day.To, out var to))
        {
            // Read() already validated, so this is unreachable through the resolver — but a caller that built
            // an EffectiveWorkingHours by hand must not get a silent pass.
            reason = $"Horaires illisibles pour le {WorkingHoursSerializer.FrenchDay(dayName)}.";
            return false;
        }

        var startTime = localStart.TimeOfDay;
        var endTime = localEnd.TimeOfDay;
        if (startTime < from || endTime > to)
        {
            reason = $"Le {WorkingHoursSerializer.FrenchDay(dayName)}, le cabinet est ouvert de "
                + $"{day.From} à {day.To}. Le rendez-vous demandé ({localStart:HH\\:mm}–{localEnd:HH\\:mm}) "
                + "est en dehors de ces horaires.";
            return false;
        }

        // The mid-day closure. Overlap, not containment: an 11:30–12:30 visit straddling a 12:00 closure is just
        // as much outside the open hours as one wholly inside it, and only the overlap test catches both.
        if (WorkingHoursSerializer.TryParseTime(day.BreakFrom, out var breakFrom)
            && WorkingHoursSerializer.TryParseTime(day.BreakTo, out var breakTo)
            && startTime < breakTo
            && endTime > breakFrom)
        {
            reason = $"Le {WorkingHoursSerializer.FrenchDay(dayName)}, le cabinet est fermé de "
                + $"{day.BreakFrom} à {day.BreakTo}. Le rendez-vous demandé "
                + $"({localStart:HH\\:mm}–{localEnd:HH\\:mm}) tombe pendant cette pause.";
            return false;
        }

        return true;
    }
}
