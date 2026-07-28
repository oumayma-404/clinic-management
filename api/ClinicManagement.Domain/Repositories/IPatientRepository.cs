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
    Task<Patient?> GetByIdWithAppointmentsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Patient>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <param name="includeArchived">
    /// Archived patients are excluded by default: they must not appear in the list, the header search, the recall
    /// list or any patient picker. Reads that legitimately need them — the deletion pre-check, « Solde patient »,
    /// direct navigation — opt in.
    /// </param>
    Task<IEnumerable<Patient>> GetByClinicIdAsync(Guid clinicId, bool includeArchived = false, CancellationToken cancellationToken = default);

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
    Task<IReadOnlyList<RecallCandidate>> GetRecallCandidatesAsync(
        Guid clinicId, DateTime anchorOnOrBeforeUtc, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Counts everything attached to a patient, so a refusal can name what actually blocks it.</summary>
    Task<PatientLinkedDataCounts> GetLinkedDataCountsAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>Unpaid balance and upcoming visits — the two things archiving must not hide.</summary>
    Task<PatientArchiveBlockers> GetArchiveBlockersAsync(Guid patientId, DateTime asOfUtc, CancellationToken cancellationToken = default);
    Task<int> CountByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<int> CountFlaggedByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Patient>> GetFlaggedPatientsAsync(CancellationToken cancellationToken = default);
    Task<Patient> AddAsync(Patient patient, CancellationToken cancellationToken = default);
    Task UpdateAsync(Patient patient, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddMedicalHistoryEntryAsync(PatientMedicalHistory entry, CancellationToken cancellationToken = default);
    Task AddFamilyHistoryEntryAsync(PatientFamilyHistory entry, CancellationToken cancellationToken = default);
}



