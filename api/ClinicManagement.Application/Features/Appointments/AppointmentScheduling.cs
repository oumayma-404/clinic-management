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
    /// <remarks>
    /// Public because the recurring-series command loads its own window once for the whole series and must back it
    /// off by the same amount, or a long appointment starting before the series' first occurrence would be invisible
    /// to the very check L2b added. One constant, two readers — a second literal there is how the widened window
    /// would silently apply to one path only.
    /// </remarks>
    public static readonly TimeSpan MaxCredibleAppointmentLength = TimeSpan.FromDays(7);

    /// <summary>
    /// True when a booking with no practitioner competes with <paramref name="candidateDoctorId"/>'s window.
    ///
    /// <para>
    /// <b>The rule, stated once (L2b).</b> An appointment with no <c>DoctorId</c> is not "nobody's" — it is
    /// <b>everybody's</b>: it is how this product expresses a « créneau occupé » block, a lunch break, a machine
    /// being serviced. So:
    /// </para>
    /// <list type="bullet">
    ///   <item>a candidate with <b>no</b> practitioner collides with <b>anything</b> in the clinic — that is what
    ///   makes blocking a period actually block it;</item>
    ///   <item>a candidate <b>with</b> a practitioner collides with that practitioner's own bookings <b>and</b>
    ///   with the clinic-wide unassigned ones.</item>
    /// </list>
    ///
    /// <para>
    /// ⚠️ This deliberately **diverges from the database's exclusion constraint**, which is predicated on
    /// <c>DoctorId IS NOT NULL</c> and therefore cannot see either case. The divergence is one-directional and
    /// safe: this guard refuses a superset of what the constraint refuses, never the other way round, so the two
    /// can never disagree about a booking that reaches the database. It stays advisory — a refusal carries
    /// <see cref="SlotTakenCode"/> and the caller may proceed with <c>AllowOverlap</c> — because a second chair
    /// is real and a hard prohibition here would describe a day the practice is not having.
    /// </para>
    ///
    /// <para>
    /// What this replaced: <c>if (!doctorId.HasValue) return null;</c>, justified as "an unassigned busy slot
    /// belongs to nobody". Two blockers rode on it. A **recurring series** never names a practitioner (the create
    /// form had no such field), so its two collision branches were gated on a value that was always null and the
    /// outcome panel's conflict list was unreachable code — a twelve-week series booked straight over twelve
    /// existing patients. And a « créneau occupé » block promised « Aucun patient ne pourra être assigné à cette
    /// période » while preventing nothing at all.
    /// </para>
    /// </summary>
    public static bool CompetesFor(Guid? candidateDoctorId, Guid? existingDoctorId) =>
        !candidateDoctorId.HasValue || !existingDoctorId.HasValue || existingDoctorId == candidateDoctorId;

    /// <summary>
    /// True when the candidate window collides with a slot-occupying appointment that competes for it — see
    /// <see cref="CompetesFor"/> for what "competes" means when either side has no practitioner.
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
        var start = NormalizeUtc(candidateStartUtc);
        var windowStart = start - MaxCredibleAppointmentLength;
        var windowEnd = start + duration;

        // Fetched clinic-wide and narrowed by CompetesFor in memory, rather than pushing `doctorId` into the
        // query: even when a practitioner IS named we must still see the clinic's unassigned blocks, and the
        // repository can only filter on equality. The window is the narrow one either way, so this reads a
        // handful of rows, not a schedule.
        var candidates = await appointments.GetByClinicIdAsync(
            clinicId, windowStart, windowEnd, doctorId: null, cancellationToken: cancellationToken);

        return candidates.FirstOrDefault(existing =>
            existing.Id != excludeAppointmentId
            && OccupiesSlot(existing.Status)
            && CompetesFor(doctorId, existing.DoctorId)
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

    /// <summary>
    /// The French refusal for a collision (AC-P1.14).
    ///
    /// <para>
    /// Two wordings, chosen from the <b>colliding</b> row rather than from the candidate: « pour ce praticien » is
    /// what makes the refusal actionable when a named dentist is already booked, and it would be simply false about
    /// an unassigned « créneau occupé » block — which, now that L2b lets those collide at all, is a case this
    /// message reaches. No new parameter, so every existing call site keeps working.
    /// </para>
    /// </summary>
    public static string SlotTakenMessage(Appointment collision) =>
        (collision.DoctorId.HasValue
            ? "Ce créneau est déjà réservé pour ce praticien "
            : "Ce créneau est déjà occupé au cabinet ")
        + $"({ClinicClock.ToClinicLocal(collision.AppointmentDateTime):dd/MM HH\\:mm}"
        + $"–{ClinicClock.ToClinicLocal(collision.AppointmentDateTime + collision.Duration):HH\\:mm}).";

    /// <summary>
    /// Machine-readable tag on the working-hours refusal, so a client can offer « Continuer quand même » and
    /// retry with <c>AllowOutsideWorkingHours</c> instead of treating it as a dead end.
    ///
    /// <para>Out-of-hours is <b>advisory, not a prohibition</b>: clinics genuinely see patients outside their
    /// posted hours (an emergency, a favour, a Saturday morning that is not in the settings yet). The override has
    /// existed on all three commands since the rule shipped, but no client ever sent it, so in practice the check
    /// read as a hard block. This code is what lets the UI complete that half-built path. (The companion column
    /// on <c>Appointment</c> that recorded the exception was deleted by AC-25 — four write sites, zero readers;
    /// the override itself is unaffected.)</para>
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
        // Null until a real practitioner is resolved — see the refusal below for why there is no placeholder name.
        string? practitioner = null;
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

        /*
         * The message names the practitioner and the closed period, per AC-P1.28. The code is what makes the
         * refusal actionable rather than terminal — see OutsideWorkingHoursCode.
         *
         * ⚠️ The name is a PREFIX only when there is one. It used to fall back to the literal « Le praticien »,
         * so an unassigned booking was refused with « <b>Le praticien :</b> Le mercredi, le cabinet est ouvert
         * de 09:00 à 17:00… » — a dangling field-name colon in front of a sentence that is already about the
         * cabinet's hours, not about anybody. With no practitioner the reason stands on its own, which reads
         * correctly and says exactly as much as is known.
         */
        return Result<bool>.Failure(
            practitioner is null ? reason! : $"{practitioner} : {reason}",
            OutsideWorkingHoursCode);
    }
}
