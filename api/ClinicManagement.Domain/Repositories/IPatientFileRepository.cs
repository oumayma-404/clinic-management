using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Repositories;

public interface IPatientFileRepository
{
    Task<Entities.PatientFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of a patient's files — <paramref name="folderId"/> null means the root, exactly as the two
    /// unbounded reads below mean it. <c>paging</c> null reads everything (the first-class unpaged case).
    /// </summary>
    Task<PagedResult<Entities.PatientFile>> GetPageAsync(
        Guid patientId,
        Guid? folderId,
        PageRequest? paging,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFile>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFile>> GetByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFile>> GetRootFilesByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task AddAsync(Entities.PatientFile file, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.PatientFile file, CancellationToken cancellationToken = default);
    Task DeleteAsync(Entities.PatientFile file, CancellationToken cancellationToken = default);
}









