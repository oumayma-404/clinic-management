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



