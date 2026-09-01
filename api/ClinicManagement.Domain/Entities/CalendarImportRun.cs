using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One Google→App import pass, recorded — and therefore undoable.
///
/// <para><b>Why a table.</b> « Importer depuis Google » was a one-click, unbounded, irreversible bulk write: it
/// returned <c>{ message, timestamp }</c>, which never said what it had done, and nothing anywhere recorded which
/// rows it had conjured. A cabinet that pressed it once acquired 97 days of its calendar as appointments — the
/// past week of them landing on « À clôturer », each demanding a présence, a fiche and an encaissement — plus a
/// placeholder patient per unmatched title, and had no way back. Cleaning up meant cancelling them, which counts
/// as a missed visit in the taux d'absence AND deletes the matching event from the practice's own Google
/// calendar. The run is what makes the question « qu'est-ce que ce clic a créé ? » answerable at all.</para>
///
/// <para><b>Why the counts are stored rather than derived.</b> A reverted run has no rows left to count, and
/// « cet import avait créé 143 rendez-vous, tous annulés le 2 septembre » is exactly what somebody will want to
/// read six months later. Membership lives on the rows (<c>Appointment.CalendarImportRunId</c>,
/// <c>Patient.CalendarImportRunId</c>); the summary lives here and outlives them.</para>
///
/// <para><b>Every pass, not every pass that created something.</b> A run that imported nothing is still recorded:
/// « l'import n'a rien trouvé » is an answer, and a missing row is indistinguishable from a pass that never ran —
/// <see cref="BackupRun"/>'s reasoning, one feature over.</para>
///
/// <para>⚠️ An <see cref="AggregateRoot{TId}"/>, so <c>AuditSaveChangesInterceptor</c> attributes both the import
/// and its undo. It writes a handful of rows a day at most, so it is not the machine noise <c>Notification</c> is
/// excluded from the ledger for.</para>
/// </summary>
public class CalendarImportRun : AggregateRoot<Guid>
{
    /// <summary>
    /// The actor recorded for a pass nobody clicked. <c>GoogleCalendarImportJob</c> runs on a schedule with no
    /// token, and <see cref="AuditEntry.UserId"/>'s own convention for that is <c>job|&lt;name&gt;</c> — reused
    /// verbatim so the two ledgers name the same actor the same way. ⚠️ A job-triggered run is revertable like
    /// any other: a pass nobody asked for is precisely the one a practice needs to be able to undo.
    /// </summary>
    public const string JobActorPrefix = "job|";

    /// <summary>The clinic whose calendar was read. Never null — the sync resolves one before it does anything.</summary>
    public Guid ClinicId { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    /// <summary>
    /// Null while the pass is running, and <b>still null if it threw</b>. That is deliberate: rows already
    /// created before the failure are stamped and revertable, and an import that fell over half-way is precisely
    /// the one worth undoing. A null here means « on ne sait pas si elle a fini », which is true.
    /// </summary>
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>The signed-in user's id, or <see cref="JobActorPrefix"/> + the job name.</summary>
    public string TriggeredByUserId { get; private set; } = string.Empty;

    /// <summary>The calendar window the pass read. Recorded so a run can explain what it could and could not see.</summary>
    public DateTime WindowFromUtc { get; private set; }

    /// <inheritdoc cref="WindowFromUtc"/>
    public DateTime WindowToUtc { get; private set; }

    /// <summary>Rows this pass conjured — the only ones an undo may delete.</summary>
    public int AppointmentsCreated { get; private set; }

    /// <inheritdoc cref="AppointmentsCreated"/>
    public int PatientsCreated { get; private set; }

    /// <summary>
    /// Rows that already existed and which the pass merely touched. Counted for the report and <b>never</b>
    /// stamped: undoing an import must not delete a booking the practice made itself.
    /// </summary>
    public int AppointmentsUpdated { get; private set; }

    /// <inheritdoc cref="AppointmentsUpdated"/>
    public int AppointmentsLinked { get; private set; }

    public DateTime? RevertedAtUtc { get; private set; }

    public string? RevertedByUserId { get; private set; }

    public int? AppointmentsDeleted { get; private set; }

    public int? PatientsDeleted { get; private set; }

    /// <summary>
    /// How many rows the undo refused to delete because real work had been recorded against them. Stored rather
    /// than recomputed for the same reason as the creation counts, and because it is the number that explains why
    /// a revert did not empty the list.
    /// </summary>
    public int? RowsKept { get; private set; }

    /// <summary>True once this run has been undone. A second undo is refused on it.</summary>
    public bool IsReverted => RevertedAtUtc.HasValue;

    private CalendarImportRun() { } // EF Core

    public CalendarImportRun(
        Guid id,
        Guid clinicId,
        string triggeredByUserId,
        DateTime startedAtUtc,
        DateTime windowFromUtc,
        DateTime windowToUtc)
        : base(id)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("La clinique est obligatoire.", nameof(clinicId));
        }

        ClinicId = clinicId;
        TriggeredByUserId = string.IsNullOrWhiteSpace(triggeredByUserId)
            ? JobActorPrefix + "unknown"
            : triggeredByUserId;
        StartedAtUtc = startedAtUtc;
        WindowFromUtc = windowFromUtc;
        WindowToUtc = windowToUtc;
    }

    /// <summary>Close the pass with what it did. Called once, at the end of a sync that did not throw.</summary>
    public void Complete(
        DateTime completedAtUtc,
        int appointmentsCreated,
        int patientsCreated,
        int appointmentsUpdated,
        int appointmentsLinked)
    {
        CompletedAtUtc = completedAtUtc;
        AppointmentsCreated = appointmentsCreated;
        PatientsCreated = patientsCreated;
        AppointmentsUpdated = appointmentsUpdated;
        AppointmentsLinked = appointmentsLinked;
    }

    /// <summary>
    /// Record that this run has been undone.
    ///
    /// <para>⚠️ The guard is the whole safety of a second press: without it two admins clicking « Annuler » at
    /// once would each delete what the other had already taken, and the second would report a revert that removed
    /// nothing. The caller checks <see cref="IsReverted"/> first and returns a coded failure; this throws for the
    /// race that slips between the check and the save, where <c>Version</c>'s concurrency token takes over.</para>
    /// </summary>
    public void MarkReverted(
        DateTime revertedAtUtc, string revertedByUserId, int appointmentsDeleted, int patientsDeleted, int rowsKept)
    {
        if (RevertedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Cet import a déjà été annulé.");
        }

        RevertedAtUtc = revertedAtUtc;
        RevertedByUserId = revertedByUserId;
        AppointmentsDeleted = appointmentsDeleted;
        PatientsDeleted = patientsDeleted;
        RowsKept = rowsKept;
    }
}
