namespace ClinicManagement.Domain.Repositories;

public interface IMedicalDocumentRepository
{
    Task<Entities.MedicalDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.MedicalDocument>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.MedicalDocument>> GetByDocumentTypeAsync(string documentType, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.MedicalDocument>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Entities.MedicalDocument document, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.MedicalDocument document, CancellationToken cancellationToken = default);
    Task DeleteAsync(Entities.MedicalDocument document, CancellationToken cancellationToken = default);
}

