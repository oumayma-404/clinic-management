namespace ClinicManagement.Domain.Repositories;

public interface IMedicalDocumentRepository
{
    Task<Entities.MedicalDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which clinic owns this document, read <b>independently of the current tenant scope</b>. It exists for the
    /// unauthenticated <c>PdfGenerationJob</c>, which has to establish a scope before it can read anything and
    /// therefore cannot get the answer from the ordinary filtered path: a <c>MedicalDocument</c> carries no
    /// <c>ClinicId</c> of its own and its owning <c>Patient</c> is clinic-filtered. Null when the document, or
    /// its patient, does not exist.
    /// </summary>
    Task<Guid?> GetOwningClinicIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.MedicalDocument>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.MedicalDocument>> GetByDocumentTypeAsync(string documentType, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.MedicalDocument>> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.MedicalDocument>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The ordonnances belonging to a set of fiches de soins — <b>one</b> query for a patient's whole séance
    /// history, never one per row: the patient page reads every fiche in a single pass because four other
    /// things on that screen need the full list.
    ///
    /// <para>
    /// ⚠️ <b>Both of a séance's ordonnance types come back from this one call</b> — the médicament
    /// <c>prescription</c> and the <c>examens</c> demande — because a fiche may own one of each and splitting
    /// this into two reads would double every page load to answer half a question each time. Callers pick by
    /// <c>DocumentType</c>.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <paramref name="appointmentIds"/> is the legacy half and it is not optional. Ordonnances written
    /// before <c>MedicalDocument.DentalRecordId</c> existed carry only an <c>AppointmentId</c>, and nothing was
    /// backfilled, so a document is claimed by a fiche either because it names that fiche <i>or</i> because it
    /// names no fiche at all and shares the fiche's appointment. Matching on the appointment first, or without
    /// the null test, would let a new ordonnance be claimed by a second fiche of the same visit.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Entities.MedicalDocument>> GetFicheOrdonnancesForDentalRecordsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> dentalRecordIds,
        IReadOnlyCollection<Guid> appointmentIds,
        CancellationToken cancellationToken = default);
    Task AddAsync(Entities.MedicalDocument document, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.MedicalDocument document, CancellationToken cancellationToken = default);
    Task DeleteAsync(Entities.MedicalDocument document, CancellationToken cancellationToken = default);
}

