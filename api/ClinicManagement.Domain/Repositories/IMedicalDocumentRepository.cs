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
    Task AddAsync(Entities.MedicalDocument document, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.MedicalDocument document, CancellationToken cancellationToken = default);
    Task DeleteAsync(Entities.MedicalDocument document, CancellationToken cancellationToken = default);
}

