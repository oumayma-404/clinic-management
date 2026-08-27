using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One cabinet's current activity figures — the row the portfolio list JOINs (<c>platform-console</c> AC-2.1,
/// AC-2.4a, EC-11).
///
/// <para><b>Why this exists beside <see cref="ClinicActivityDay"/>.</b> The list filters on « dormant » and sorts
/// on activity, so those figures must be columns the database can filter and order <b>before</b> the page is
/// cut. Folding thirty day rows per cabinet inside the list query would put the aggregation on the hot path of
/// every keystroke; folding it after the page is cut would answer about the page. One row per cabinet, rewritten
/// by each pass, makes filter + sort + page **one bounded query**.</para>
///
/// <para><b>Not every figure here is audit-derived, and one of them must not be.</b> <see cref="Patients"/> is a
/// <c>COUNT</c> over the cabinet's patients — never a count of audit <c>Insert</c> rows: the ledger only exists
/// since <c>adoption-qa-i</c>, so every patient recorded before it has no row and an established practice would
/// read as nearly empty. That is a figure wrong in the direction of « this cabinet is barely used », which is
/// precisely the churn signal the list exists to give.</para>
///
/// <para>⚠️ <see cref="CollectedThisMonth"/> makes the console the <b>fifth</b> money read in this product. It is
/// computed from the same repository predicates la caisse sums, through <c>PlanBillingRules.BilledPlanIds</c> —
/// never a hand-written <c>SUM</c> here. The vendor quoting a cabinet its own turnover from a figure the
/// cabinet's own caisse contradicts is the worst possible place for that drift, which is why
/// <c>MoneyReadConsistencyTests</c> pins the two equal.</para>
/// </summary>
public class ClinicActivitySnapshot : Entity<Guid>
{
    public Guid ClinicId { get; private set; }

    /// <summary>Saves by people at the cabinet in the last 7 clinic-local days.</summary>
    public int Writes7d { get; private set; }

    /// <summary>Saves by people at the cabinet in the last 30 clinic-local days. « Dormant » is this at zero (AC-2.3).</summary>
    public int Writes30d { get; private set; }

    /// <summary>Appointments booked in the last 30 clinic-local days.</summary>
    public int Appointments30d { get; private set; }

    /// <summary>How many of the last 30 clinic-local days saw any save at all — the figure that tells a cabinet
    /// used daily from one that had a single busy afternoon.</summary>
    public int ActiveDays30d { get; private set; }

    /// <summary>The most recent save by anyone at the cabinet, or null where there has never been one.</summary>
    public DateTime? LastWriteAt { get; private set; }

    /// <summary>Total patients on file — a <c>COUNT</c>, not an audit derivation. See the class remarks.</summary>
    public int Patients { get; private set; }

    /// <summary>Staff accounts at the cabinet.</summary>
    public int Users { get; private set; }

    /// <summary>The most recent sign-in by any of them, or null where nobody has ever signed in.</summary>
    public DateTime? LastLoginAt { get; private set; }

    /// <summary>
    /// What the <b>cabinet itself</b> collected in the current clinic-local month, in dinars — « encaissé par le
    /// cabinet ». Deliberately never confusable with the vendor's own revenue (AC-2.7): that one is a separate
    /// figure on the summary, and the two are labelled apart on screen.
    /// </summary>
    public decimal CollectedThisMonth { get; private set; }

    /// <summary>
    /// When the pass that wrote this row ran — the whole of AC-2.8. Every figure above is only as fresh as this,
    /// and the screen states it beside them: a stale figure presented as live is how a cabinet that started
    /// working yesterday gets a churn call today.
    /// </summary>
    public DateTime ComputedAt { get; private set; }

    private ClinicActivitySnapshot() { } // For EF Core

    public ClinicActivitySnapshot(Guid clinicId)
        : base(Guid.NewGuid())
    {
        if (clinicId == Guid.Empty)
            throw new ArgumentException("Un cabinet est requis pour un instantané d'activité.", nameof(clinicId));

        ClinicId = clinicId;
    }

    /// <summary>
    /// Rewrites every figure, including <see cref="ComputedAt"/>. One method rather than per-figure setters
    /// because a snapshot half-refreshed is a row whose parts describe different moments — and nothing on screen
    /// could say which figure was which.
    /// </summary>
    public void Restate(
        int writes7d,
        int writes30d,
        int appointments30d,
        int activeDays30d,
        DateTime? lastWriteAt,
        int patients,
        int users,
        DateTime? lastLoginAt,
        decimal collectedThisMonth,
        DateTime computedAt)
    {
        Writes7d = NotNegative(writes7d, nameof(writes7d));
        Writes30d = NotNegative(writes30d, nameof(writes30d));
        Appointments30d = NotNegative(appointments30d, nameof(appointments30d));
        ActiveDays30d = NotNegative(activeDays30d, nameof(activeDays30d));
        LastWriteAt = lastWriteAt;
        Patients = NotNegative(patients, nameof(patients));
        Users = NotNegative(users, nameof(users));
        LastLoginAt = lastLoginAt;
        CollectedThisMonth = collectedThisMonth;
        ComputedAt = computedAt;
    }

    private static int NotNegative(int value, string name) =>
        value >= 0 ? value : throw new ArgumentOutOfRangeException(name, "Un compteur d'activité ne peut pas être négatif.");
}
