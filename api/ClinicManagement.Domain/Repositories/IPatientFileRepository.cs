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
    /// <summary>
    /// One page of <b>every</b> file the clinic holds, joined to its patient's name
    /// (<c>patient-file-mirror</c>). The one read in this interface that is not about a single patient.
    ///
    /// <para>⚠️ <paramref name="clinicId"/> is a real predicate, not documentation. Every other read here leans on
    /// the ambient tenant filter, which is correct — but an `Unset` scope reads zero rows with no error, and this
    /// is the read that would answer « ce cabinet n'a aucun fichier » to a machine whose job is to notice exactly
    /// that. Stated explicitly, it also gives the cross-clinic guard test something to assert against.</para>
    ///
    /// <para>⚠️ Ordered <b>ascending</b> on <c>UploadedAt</c> then <c>Id</c>, against the convention of every
    /// other list in this product. A caller walking the pages is racing uploads: with newest-first, a file
    /// arriving mid-walk pushes every later row down one and the walk skips one it had not read yet. Ascending,
    /// a new file lands after the last page and is simply picked up next time.</para>
    /// </summary>
    /// <summary>
    /// How many of the cabinet's files live in its coffre rather than on the server (<c>clinic-file-vault</c>).
    ///
    /// <para>⚠️ It exists so the staleness alert can ask « is there anything to lose? » first. A cabinet with an
    /// empty coffre must never be nagged about not copying it — a warning about an empty folder is the fastest way
    /// to teach an owner to dismiss this one, and then the real warning goes with it.</para>
    /// </summary>
    Task<int> CountVaultFilesAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task<PagedResult<ClinicFileManifestRow>> GetClinicManifestPageAsync(
        Guid clinicId,
        PageRequest? paging,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Entities.PatientFile>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFile>> GetByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entities.PatientFile>> GetRootFilesByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task AddAsync(Entities.PatientFile file, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entities.PatientFile file, CancellationToken cancellationToken = default);
    Task DeleteAsync(Entities.PatientFile file, CancellationToken cancellationToken = default);
}









