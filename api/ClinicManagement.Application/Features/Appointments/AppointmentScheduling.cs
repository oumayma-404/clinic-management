using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// The shared scheduling rules every appointment writer reads: does this window overlap another booking, and is
/// it inside the practitioner's working hours.
/// <para>
/// <b>AC-P1.39</b> — the overlap predicate existed in <b>three</b> copies (create, update, recurring) and they
/// had already drifted: the recurring one excluded only <c>Cancelled</c> and not <c>NoShow</c>, so a series
/// silently refused to book over a slot the single-appointment path considered free. One helper, one rule.
/// </para>
/// </summary>
public static class AppointmentScheduling
{
    /// <summary>
    /// Statuses that still occupy a slot. <c>Cancelled</c> and <c>NoShow</c> do not — rebooking a freed slot is
    /// the most common scheduling action there is, and this set is deliberately the same one the database's
    /// partial exclusion constraint uses (<c>Status NOT IN (5,6)</c>) so the guard and the constraint agree.
    /// </summary>
    public static bool OccupiesSlot(AppointmentStatus status) =>
        status != AppointmentStatus.Cancelled && status != AppointmentStatus.NoShow;

    /// <summary>Half-open interval overlap: <c>[aStart, aEnd)</c> against <c>[bStart, bEnd)</c>.</summary>
    public static bool Overlaps(DateTime aStart, TimeSpan aDuration, DateTime bStart, TimeSpan bDuration) =>
        aStart < bStart + bDuration && bStart < aStart + aDuration;

    /// <summary>
    /// UTC, with <see cref="DateTimeKind.Unspecified"/> read as UTC — matching how
    /// <c>ApplicationDbContext</c> persists it, so the collision maths and the stored value agree.
    /// </summary>
    public static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    /// <summary>
    /// Load every appointment for this practitioner that <b>could</b> overlap the candidate window.
    /// <para>
    /// <b>AC-P1.21 (A-3)</b>: the window used to start at <c>candidateStart − 1 day</c>, a heuristic that misses
    /// a long appointment beginning more than 24 h earlier. The repository can only filter on the start column
    /// (<c>Duration</c> is <c>bigint</c> ticks, so <c>start + duration</c> is not queryable), so the fix is to
    /// widen the back-off to a bound no real appointment exceeds and keep the exact test in memory.
    /// </para>
    /// </summary>
    private static readonly TimeSpan MaxCredibleAppointmentLength = TimeSpan.FromDays(7);

    /// <summary>
    /// True when the candidate window collides with a slot-occupying appointment for the same practitioner.
    /// </summary>
    /// <param name="excludeAppointmentId">The appointment being edited, so it cannot collide with itself.</param>
    public static async Task<Appointment?> FindCollisionAsync(
        IAppointmentRepository appointments,
        Guid clinicId,
        Guid? doctorId,
        DateTime candidateStartUtc,
        TimeSpan duration,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken)
    {
        // No practitioner means no double-booking to prevent: an unassigned "busy slot" belongs to nobody. This
        // matches the exclusion constraint's stated NULL rule (AC-P1.17).
        if (!doctorId.HasValue)
        {
            return null;
        }

        var start = NormalizeUtc(candidateStartUtc);
        var windowStart = start - MaxCredibleAppointmentLength;
        var windowEnd = start + duration;

        var candidates = await appointments.GetByClinicIdAsync(
            clinicId, windowStart, windowEnd, doctorId, cancellationToken);

        return candidates.FirstOrDefault(existing =>
            existing.Id != excludeAppointmentId
            && OccupiesSlot(existing.Status)
            && Overlaps(existing.AppointmentDateTime, existing.Duration, start, duration));
    }

    /// <summary>
    /// Machine-readable tag on the collision refusal, so a client can offer « Continuer quand même » and retry with
    /// <c>AllowOverlap</c> instead of treating it as a dead end — exactly as
    /// <see cref="OutsideWorkingHoursCode"/> already does for the working-hours rule.
    ///
    /// <para>A double-booking is <b>advisory, not a prohibition</b>: a second chair, an assistant preparing one
    /// patient while the dentist starts another, an emergency squeezed into a taken slot. The old behaviour refused
    /// outright, which made the software describe a day the practice was not having.</para>
    ///
    /// <para>A code rather than the French message, for the same reason as the working-hours one: the message names
    /// the colliding window and is reworded freely, so matching on its text would turn the confirm dialog back into
    /// a hard block the first time somebody edits a sentence.</para>
    /// </summary>
    public const string SlotTakenCode = "slot_taken";

    /// <summary>The French refusal for a collision (AC-P1.14).</summary>
    public static string SlotTakenMessage(Appointment collision) =>
        $"Ce créneau est déjà réservé pour ce praticien "
        + $"({ClinicClock.ToClinicLocal(collision.AppointmentDateTime):dd/MM HH\\:mm}"
        + $"–{ClinicClock.ToClinicLocal(collision.AppointmentDateTime + collision.Duration):HH\\:mm}).";

    /// <summary>
    /// Machine-readable tag on the working-hours refusal, so a client can offer « Continuer quand même » and
    /// retry with <c>AllowOutsideWorkingHours</c> instead of treating it as a dead end.
    ///
    /// <para>Out-of-hours is <b>advisory, not a prohibition</b>: clinics genuinely see patients outside their
    /// posted hours (an emergency, a favour, a Saturday morning that is not in the settings yet). The override has
    /// existed on all three commands since the rule shipped, and <c>Appointment.MarkBookedOutsideWorkingHours</c>
    /// records that it was a deliberate exception — but no client ever sent the flag, so in practice the check
    /// read as a hard block. This code is what lets the UI complete that half-built path.</para>
    ///
    /// <para>A code rather than the French message, because the message names the practitioner and the closed
    /// period and is reworded freely; matching on it would make the confirm dialog silently revert to a hard block
    /// the first time somebody edits a sentence in <c>WorkingHoursResolver</c>.</para>
    /// </summary>
    public const string OutsideWorkingHoursCode = "outside_working_hours";

    /// <summary>
    /// Check the candidate window against the practitioner's resolved working hours (AC-P1.28).
    /// <para>
    /// Returns success when nothing is configured anywhere — a clinic that has never opened the settings screen
    /// is unaffected, which is <b>R-12</b>'s safety valve. When it does refuse, the caller may still proceed by
    /// recording an explicit override (AC-P1.31), which is why this returns a reason rather than throwing — and
    /// why the failure carries <see cref="OutsideWorkingHoursCode"/>, so the client can offer that override.
    /// </para>
    /// </summary>
    public static async Task<Result<bool>> CheckWorkingHoursAsync(
        IDoctorRepository doctors,
        IClinicRepository clinics,
        Guid clinicId,
        Guid? doctorId,
        DateTime candidateStartUtc,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var clinic = await clinics.GetByIdAsync(clinicId, cancellationToken);

        string? doctorHoursJson = null;
        var practitioner = "Le praticien";
        if (doctorId.HasValue)
        {
            var doctor = await doctors.GetByIdAsync(doctorId.Value, cancellationToken);
            if (doctor != null && doctor.ClinicId == clinicId)
            {
                doctorHoursJson = doctor.WorkingHoursJson;
                practitioner = doctor.FullName;
            }
        }

        var hours = WorkingHoursResolver.Resolve(doctorHoursJson, clinic?.WorkingHoursJson);
        if (WorkingHoursResolver.IsWithin(hours, NormalizeUtc(candidateStartUtc), duration, out var reason))
        {
            return Result<bool>.Success(true);
        }

        // The message names the practitioner and the closed period, per AC-P1.28. The code is what makes the
        // refusal actionable rather than terminal — see OutsideWorkingHoursCode.
        return Result<bool>.Failure($"{practitioner} : {reason}", OutsideWorkingHoursCode);
    }
}
