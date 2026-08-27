using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IDentalRecordRepository
{
    Task<DentalRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<DentalRecord>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One row per fiche de soins documenting one of <paramref name="appointmentIds"/>: the visit, the record,
    /// and the record's derived <c>Cost</c>. The third sibling of <c>IInvoiceRepository</c>'s
    /// <c>GetAppointmentLinksAsync</c>, answering « cette séance a-t-elle une fiche, et valait-elle quelque
    /// chose ? ».
    ///
    /// <para><b>Bounded by the id set</b>, exactly like that sibling and for its reason: the caller has a date
    /// window, and reading every appointment-linked fiche the clinic has ever recorded in order to annotate one
    /// week of agenda grows without limit.</para>
    ///
    /// <para><c>Cost</c> travels with the link because a fiche worth <c>0</c> is « rien à facturer » derived —
    /// a contrôle gratuit — and fetching it separately would mean a second read per row for a figure the same
    /// projection already has in hand.</para>
    ///
    /// <para>A visit may legitimately have <b>several</b> fiches (the link is neither required nor unique), so
    /// the caller groups rather than assuming one.</para>
    /// </summary>
    Task<IReadOnlyList<(Guid AppointmentId, Guid DentalRecordId, decimal Cost)>> GetAppointmentLinksAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> appointmentIds,
        CancellationToken cancellationToken = default);
    Task<DentalRecord> AddAsync(DentalRecord dentalRecord, CancellationToken cancellationToken = default);
    Task UpdateAsync(DentalRecord dentalRecord, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}









