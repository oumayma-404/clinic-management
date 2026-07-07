namespace ClinicManagement.Domain.Repositories;

public interface IPatientFolderRepository
{
    Task<Entities.PatientFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFolder>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFolder>> GetRootFoldersByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFolder>> GetSubFoldersAsync(Guid parentFolderId, CancellationToken cancellationToken = default);
    Task<Entities.PatientFolder?> GetByNameAndPatientIdAsync(string name, Guid patientId, CancellationToken cancellationToken = default);
    Task AddAsync(Entities.PatientFolder folder, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.PatientFolder folder, CancellationToken cancellationToken = default);
    Task DeleteAsync(Entities.PatientFolder folder, CancellationToken cancellationToken = default);
}


