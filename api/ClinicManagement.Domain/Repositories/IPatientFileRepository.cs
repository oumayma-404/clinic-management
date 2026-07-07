namespace ClinicManagement.Domain.Repositories;

public interface IPatientFileRepository
{
    Task<Entities.PatientFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFile>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFile>> GetByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFile>> GetRootFilesByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task AddAsync(Entities.PatientFile file, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.PatientFile file, CancellationToken cancellationToken = default);
    Task DeleteAsync(Entities.PatientFile file, CancellationToken cancellationToken = default);
}









