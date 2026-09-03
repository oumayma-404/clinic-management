using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Uploads still arriving. Clinic-scoped like everything else a cabinet owns, so a read with no clinic in scope
/// returns nothing rather than another practice's in-flight file.
/// </summary>
public interface IFileUploadSessionRepository
{
    Task<FileUploadSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(FileUploadSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(FileUploadSession session, CancellationToken cancellationToken = default);

    Task RemoveAsync(FileUploadSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sessions whose window has passed, oldest first and bounded.
    ///
    /// <para>⚠️ Read <b>across clinics</b>, because the sweep that reclaims their staging areas runs with no
    /// request behind it. Its caller declares <c>UseSystemWide</c>; this is not a hole in the tenant filter but
    /// the one read that legitimately needs to see past it, and it returns only what the sweep needs to delete.</para>
    /// </summary>
    Task<IReadOnlyList<FileUploadSession>> GetExpiredAsync(
        DateTime nowUtc, int max, CancellationToken cancellationToken = default);
}
