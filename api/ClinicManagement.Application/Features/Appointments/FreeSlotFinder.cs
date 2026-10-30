using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>What a day offers to a booking of a given length.</summary>
public enum FreeDayState
{
    /// <summary>At least one start is free.</summary>
    Open,

    /// <summary>The practitioner works that day and every start is taken.</summary>
    Full,

    /// <summary>No start fits the hours: a closed day, a day already over, or a booking longer than the opening.</summary>
    Closed,

    /// <summary>The practitioner is on congé.</summary>
    Leave,

    /// <summary>No hours are configured anywhere, so the booking guard accepts any time and there is no list to give.</summary>
    Unrestricted,
}

/// <param name="Date">The clinic-local day.</param>
/// <param name="Starts">The free clinic-local start times, on the quarter hour, in order.</param>
public sealed record FreeDay(DateOnly Date, FreeDayState State, IReadOnlyList<TimeOnly> Starts);

/// <summary>
/// The times a practitioner is free to take a booking of a given length — answered with <b>the booking guard's own
/// predicates</b> (<see cref="WorkingHoursResolver.IsWithin"/>, <see cref="AppointmentScheduling.OccupiesSlot"/>,
/// <see cref="AppointmentScheduling.CompetesFor"/>, <see cref="AppointmentScheduling.Overlaps"/>, the congé day
/// rule), so a start listed here is one the save accepts without an override. Shared by « Reporter » and, later, the
/// patients' online booking (<c>features/patient-online-booking/blueprint.md</c>), which maps
/// <see cref="FreeDayState.Unrestricted"/> to « no slots ».
/// </summary>
public static class FreeSlotFinder
{
    /// <summary>Starts sit on the quarter hour — the agenda's drag snaps to the same step.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(15);

    public const int MaxDays = 31;

    /// <summary>Reads the hours, the congés and the clinic-wide window once, then <see cref="Compute"/>s.</summary>
    public static async Task<IReadOnlyList<FreeDay>> FindAsync(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IClinicRepository clinics,
        IDoctorLeaveRepository leaves,
        Guid clinicId,
        Guid? doctorId,
        TimeSpan duration,
        DateOnly fromLocal,
        int days,
        Guid? excludeAppointmentId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var lastDay = fromLocal.AddDays(days - 1);

        // Exactly CheckWorkingHoursAsync's resolution: the doctor's own hours and congés only for a doctor of this clinic.
        var clinic = await clinics.GetByIdAsync(clinicId, cancellationToken);
        Doctor? doctor = null;
        if (doctorId.HasValue)
        {
            var found = await doctors.GetByIdAsync(doctorId.Value, cancellationToken);
            if (found != null && found.ClinicId == clinicId)
            {
                doctor = found;
            }
        }

        var hours = WorkingHoursResolver.Resolve(doctor?.WorkingHoursJson, clinic?.WorkingHoursJson);
        IReadOnlyCollection<DoctorLeave> doctorLeaves = doctor is null
            ? Array.Empty<DoctorLeave>()
            : (await leaves.GetOverlappingAsync(clinicId, fromLocal, lastDay, cancellationToken))
                .Where(leave => leave.DoctorId == doctor.Id)
                .ToList();

        // Clinic-wide and backed off like FindCollisionAsync: unassigned blocks compete with every practitioner, and a
        // visit starting before the window can still run into it.
        var windowStartUtc = ClinicClock.StartOfLocalDayUtc(fromLocal.ToDateTime(TimeOnly.MinValue));
        var windowEndUtc = ClinicClock.StartOfLocalDayUtc(lastDay.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var window = await appointments.GetByClinicIdAsync(
            clinicId,
            windowStartUtc - AppointmentScheduling.MaxCredibleAppointmentLength,
            windowEndUtc,
            doctorId: null,
            cancellationToken: cancellationToken);

        return Compute(hours, doctorLeaves, window.ToList(), doctorId, duration, fromLocal, days, excludeAppointmentId, nowUtc);
    }

    /// <summary>The pure core: no repository and no clock, so every rule is testable with fixed instants.</summary>
    public static IReadOnlyList<FreeDay> Compute(
        EffectiveWorkingHours hours,
        IReadOnlyCollection<DoctorLeave> doctorLeaves,
        IReadOnlyCollection<Appointment> clinicWindow,
        Guid? doctorId,
        TimeSpan duration,
        DateOnly fromLocal,
        int days,
        Guid? excludeAppointmentId,
        DateTime nowUtc)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var now = AppointmentScheduling.NormalizeUtc(nowUtc);
        var busy = clinicWindow
            .Where(existing => existing.Id != excludeAppointmentId
                && AppointmentScheduling.OccupiesSlot(existing)
                && AppointmentScheduling.CompetesFor(doctorId, existing.DoctorId))
            .ToList();

        var result = new List<FreeDay>(days);
        for (var offset = 0; offset < days; offset++)
        {
            var day = fromLocal.AddDays(offset);
            if (hours.Unrestricted)
            {
                result.Add(new FreeDay(day, FreeDayState.Unrestricted, Array.Empty<TimeOnly>()));
                continue;
            }

            var dayStartUtc = ClinicClock.StartOfLocalDayUtc(day.ToDateTime(TimeOnly.MinValue));
            var starts = new List<TimeOnly>();
            var anyOpenStart = false;
            for (var time = TimeSpan.Zero; time < TimeSpan.FromDays(1); time += Step)
            {
                var startUtc = dayStartUtc + time;
                if (startUtc < now || !WorkingHoursResolver.IsWithin(hours, startUtc, duration, out _))
                {
                    continue;
                }

                anyOpenStart = true;

                // The booking guard's congé rule, per window — IsWithin already keeps a window inside one day.
                var (firstDay, lastDay) = AppointmentScheduling.LocalDaysOf(startUtc, duration);
                if (doctorLeaves.Any(leave => leave.Covers(firstDay, lastDay)))
                {
                    continue;
                }

                if (busy.Any(existing => AppointmentScheduling.Overlaps(existing.AppointmentDateTime, existing.Duration, startUtc, duration)))
                {
                    continue;
                }

                starts.Add(TimeOnly.FromTimeSpan(time));
            }

            var state = !anyOpenStart ? FreeDayState.Closed
                : starts.Count > 0 ? FreeDayState.Open
                : doctorLeaves.Any(leave => leave.Covers(day, day)) ? FreeDayState.Leave
                : FreeDayState.Full;
            result.Add(new FreeDay(day, state, starts));
        }

        return result;
    }
}
