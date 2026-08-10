using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// What one cabinet did on one <b>clinic-local</b> calendar day (<c>platform-console</c> FR-3, AC-2.4a).
///
/// <para><b>Why activity is stored at all, rather than derived per request.</b> The portfolio can be filtered
/// and sorted <i>on activity</i>, so every figure has to exist for every cabinet <b>before</b> a page is cut —
/// a figure computed over the page already selected would filter and sort a window rather than the portfolio
/// (AC-2.4a). Deriving it per request would also mean scanning the mutation ledger of every practice on every
/// keystroke, which is bounded by the busiest cabinet's whole history rather than by the number of cabinets
/// (EC-11).</para>
///
/// <para><b>Why a day row exists beside <see cref="ClinicActivitySnapshot"/>.</b> The snapshot is what the list
/// JOINs — one row per cabinet, rewritten every pass. This is the durable history: the six-month trend reads
/// it, and it is what survives any later retention policy on the audit ledger the counters were derived from.
/// A snapshot alone could not answer « was this cabinet busier in March? » once the underlying rows are gone.
/// </para>
///
/// <para>⚠️ <b><see cref="Day"/> is a <see cref="DateOnly"/>, and that is not a stylistic choice.</b> It is a
/// calendar day in Tunis, not an instant: the context maps every <c>DateTime</c> through a UTC converter, so a
/// day stored as one would be shifted by an hour at exactly the boundary the figure is defined on. The job
/// derives the day through <c>ClinicClock</c> and stores the answer, not the arithmetic.</para>
///
/// <para>⚠️ <b>A cabinet with nothing to count gets a row of zeros, never no row</b> (EC-8). « Aucune activité »
/// is a real and useful answer — it is the churn signal the portfolio exists to give — while a missing row is
/// indistinguishable from a pass that never ran.</para>
/// </summary>
public class ClinicActivityDay : Entity<Guid>
{
    public Guid ClinicId { get; private set; }

    /// <summary>The clinic-local calendar day these counts belong to.</summary>
    public DateOnly Day { get; private set; }

    /// <summary>
    /// Saves made by <b>people at the cabinet</b> on this day (AC-2.2) — background work and the vendor's own
    /// console writes are excluded upstream, by <c>PlatformCounterPass</c>, on <c>AuditActor</c>'s own prefixes.
    /// </summary>
    public int Writes { get; private set; }

    /// <summary>Appointments <b>booked</b> on this day. See <c>PlatformCounterPass</c> on why booked, not held.</summary>
    public int Appointments { get; private set; }

    /// <summary>Patients recorded on this day. The portfolio's <i>total</i> patient count is counted differently — see
    /// <see cref="ClinicActivitySnapshot.Patients"/>.</summary>
    public int PatientsCreated { get; private set; }

    /// <summary>When the pass that wrote this row ran. Kept per row so a re-run is visible rather than silent.</summary>
    public DateTime ComputedAt { get; private set; }

    private ClinicActivityDay() { } // For EF Core

    public ClinicActivityDay(Guid clinicId, DateOnly day, int writes, int appointments, int patientsCreated, DateTime computedAt)
        : base(Guid.NewGuid())
    {
        if (clinicId == Guid.Empty)
            throw new ArgumentException("Un cabinet est requis pour une journée d'activité.", nameof(clinicId));

        ClinicId = clinicId;
        Day = day;
        Restate(writes, appointments, patientsCreated, computedAt);
    }

    /// <summary>
    /// Overwrites the counts for this day. The pass is idempotent by design — re-running it for a day must
    /// produce the same row rather than a second one, which is what the unique index on (cabinet, day) holds.
    /// </summary>
    public void Restate(int writes, int appointments, int patientsCreated, DateTime computedAt)
    {
        Writes = NotNegative(writes, nameof(writes));
        Appointments = NotNegative(appointments, nameof(appointments));
        PatientsCreated = NotNegative(patientsCreated, nameof(patientsCreated));
        ComputedAt = computedAt;
    }

    private static int NotNegative(int value, string name) =>
        value >= 0 ? value : throw new ArgumentOutOfRangeException(name, "Un compteur d'activité ne peut pas être négatif.");
}
