using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Everything attached to a patient that must block their deletion, counted in one pass.
///
/// <c>Invoices</c> and <c>TreatmentPlans</c> are the reason this exists as an explicit count rather than a
/// caught <c>DbUpdateException</c>: neither has a foreign key to <c>Patients</c>, so no database constraint has
/// ever fired for them — a patient with ten invoices and nothing else deleted cleanly and orphaned all ten.
/// </summary>
public sealed record PatientLinkedDataCounts(
    int Appointments,
    int Invoices,
    int TreatmentPlans,
    int DentalRecords,
    int ToothStates,
    int MedicalDocuments,
    int Files,
    int Folders,
    int Flags,
    int RecurringAppointments,
    int MedicalHistoryEntries,
    int FamilyHistoryEntries,
    int LabOrders,
    int WaitingListEntries,
    int Notifications)
{
    public int Total =>
        Appointments + Invoices + TreatmentPlans + DentalRecords + ToothStates + MedicalDocuments
        + Files + Folders + Flags + RecurringAppointments + MedicalHistoryEntries + FamilyHistoryEntries
        + LabOrders + WaitingListEntries + Notifications;

    public bool Any => Total > 0;
}

/// <summary>
/// What stops a patient being archived. Archiving hides someone; it must not be a way to make an unpaid balance
/// or an upcoming visit quietly disappear from « Créances » and the calendar.
/// </summary>
public sealed record PatientArchiveBlockers(
    decimal InvoiceOutstanding,
    decimal InstallmentOutstanding,
    int FutureAppointments)
{
    public decimal TotalOutstanding => InvoiceOutstanding + InstallmentOutstanding;

    public bool Any => TotalOutstanding > 0m || FutureAppointments > 0;
}

/// <summary>
/// One row of the bounded « patients à relancer » read (AC-P4.41). A projection, not a <see cref="Patient"/>:
/// the relance list needs six scalars and the patient's last completed visit, and materialising whole
/// aggregates plus every appointment in the clinic to derive them is what the finding was about.
///
/// <para><paramref name="RecallAnchorUtc"/> is the date the due date is measured from — the last
/// <c>Completed</c> appointment, or the patient's creation date when they have never been seen. It is returned
/// rather than resolved into a due date because <c>AddMonths</c>'s end-of-month clamping is not reproducible as
/// an inverted SQL comparison, so the caller applies the interval itself (see the repository implementation).
/// </para>
/// </summary>
public sealed record RecallCandidate(
    Guid PatientId,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    DateTime RecallAnchorUtc,
    DateTime? LastCompletedVisitUtc,
    string? RecallReason,
    DateTime? LastRecallContactedAt);

public interface IPatientRepository
{
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The patients behind a set of ids, in one round trip and already narrowed to the clinic (AC-P6.21).
    /// Written for « Créances », which resolved a name per row with a <c>GetByIdAsync</c> inside its merge loop —
    /// one query per patient with a balance, on the screen a clinic opens to chase money.
    /// <para>
    /// The clinic filter is a parameter rather than the caller's job because the whole point of the batch is that
    /// the caller no longer sees each aggregate individually; a per-row <c>ClinicId</c> check it can silently
    /// forget is worse than one it cannot express. Ids outside the clinic are simply absent from the result.
    /// </para>
    /// <returns>A lookup keyed by patient id. Archived patients are included — a balance still has to be chased.</returns>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Patient>> GetByIdsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default);
    Task<Patient?> GetByIdWithAppointmentsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Patient>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <param name="includeArchived">
    /// Archived patients are excluded by default: they must not appear in the list, the header search, the recall
    /// list or any patient picker. Reads that legitimately need them — the deletion pre-check, « Solde patient »,
    /// direct navigation — opt in.
    /// </param>
    /// <param name="createdFrom">
    /// Inclusive lower bound on <c>CreatedAt</c>. Backs the dashboard's « Nouveaux patients » drill-through
    /// (<c>/patients?createdFrom=&amp;createdTo=</c>) so the click lands on exactly the patients the KPI counted.
    /// Deliberately added to this signature rather than as a parallel <c>GetByClinicIdWithDatesAsync</c>: the
    /// method already carries one optional filter, and a near-duplicate read is how two list queries drift.
    /// </param>
    /// <param name="createdTo">Inclusive upper bound on <c>CreatedAt</c>.</param>
    /// <param name="searchTerm">
    /// Free-text filter over first name, last name, « prénom nom » and phone, matched case- and
    /// accent-insensitively <b>in SQL</b>. It moved here from the handler when the list was paginated: an
    /// in-memory filter can only see the rows already fetched, which before paging happened to be all of them
    /// and now is one page — so the same code would answer « aucun patient » for anyone not on it.
    /// </param>
    /// <param name="paging">
    /// The page to return, or <c>null</c> for every matching row. Unbounded is a real case here — the header
    /// search, the patient pickers and the AI dispatcher all need the full set.
    /// </param>
    Task<PagedResult<Patient>> GetByClinicIdAsync(
        Guid clinicId,
        bool includeArchived = false,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        string? searchTerm = null,
        bool flaggedOnly = false,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The bounded candidate set for « patients à relancer » (AC-P4.41–4.43). Everything expressible in SQL is
    /// applied in SQL — clinic scope, archived exclusion, active snooze, a future Scheduled/Confirmed booking,
    /// the last completed visit per patient, and an upper bound on the recall anchor — so the handler no longer
    /// reads every patient and every appointment in the clinic.
    /// </summary>
    /// <param name="anchorOnOrBeforeUtc">
    /// Conservative upper bound on <see cref="RecallCandidate.RecallAnchorUtc"/>. Deliberately a <b>superset</b>
    /// of the real rule: the exact <c>anchor + interval &lt;= now</c> test is applied by the caller, because
    /// inverting <c>AddMonths</c> into SQL would change which patients qualify at month boundaries (AC-P4.42).
    /// </param>
    /// <param name="alwaysIncludePatientIds">
    /// Patients who must come back <b>regardless of the date bound</b>, because some other reason already qualifies
    /// them for the « à rappeler » worklist — a stalled devis or an overdue échéance. Those reasons are unrelated to
    /// when the patient was last seen, so the anchor bound would hide exactly the rows worth chasing.
    /// <para>
    /// An explicit id set rather than dropping the bound: removing it would return every non-archived, non-snoozed,
    /// unbooked patient in the clinic on every page load, re-opening the § 9.6 full-scan this read was written to
    /// close (AC-P4.41). The ids come from the plan and installment reads, both naturally small. The archived /
    /// snoozed / future-booking exclusions still apply to them — being owed money does not override an archive.
    /// </para>
    /// </param>
    Task<IReadOnlyList<RecallCandidate>> GetRecallCandidatesAsync(
        Guid clinicId,
        DateTime anchorOnOrBeforeUtc,
        DateTime nowUtc,
        IReadOnlyCollection<Guid>? alwaysIncludePatientIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>Counts everything attached to a patient, so a refusal can name what actually blocks it.</summary>
    Task<PatientLinkedDataCounts> GetLinkedDataCountsAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>Unpaid balance and upcoming visits — the two things archiving must not hide.</summary>
    Task<PatientArchiveBlockers> GetArchiveBlockersAsync(Guid patientId, DateTime asOfUtc, CancellationToken cancellationToken = default);
    Task<int> CountByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<int> CountFlaggedByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many patients the clinic registered in <c>[from, toInclusive]</c> — the dashboard's « Nouveaux
    /// patients ». Archived patients are excluded by default for the same reason they are excluded from the list:
    /// the figure is a link to that list, and the two must show the same people.
    /// </summary>
    Task<int> CountCreatedBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        bool includeArchived = false,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<Patient>> GetFlaggedPatientsAsync(CancellationToken cancellationToken = default);
    Task<Patient> AddAsync(Patient patient, CancellationToken cancellationToken = default);
    Task UpdateAsync(Patient patient, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddMedicalHistoryEntryAsync(PatientMedicalHistory entry, CancellationToken cancellationToken = default);
    Task AddFamilyHistoryEntryAsync(PatientFamilyHistory entry, CancellationToken cancellationToken = default);
}



