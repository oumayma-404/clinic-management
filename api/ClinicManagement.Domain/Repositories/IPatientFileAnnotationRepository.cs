using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The markers on a 3D model (<c>mesh-interactive-viewer</c>).
///
/// <para>⚠️ <b>Every read is scoped to one file and there is deliberately no « all annotations » read.</b> A
/// marker is only meaningful beside the surface it points at, so a clinic-wide list would answer no question
/// anybody has — and it is the read that would have to grow paging, ordering and a tenant predicate of its own.
/// This is also why there is no paging here: a model carries a handful of markers, not a page of them.</para>
/// </summary>
public interface IPatientFileAnnotationRepository
{
    Task<PatientFileAnnotation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every marker on one file, oldest first — the order they were dropped in, which is the order their
    /// default names (« Repère 1 », « Repère 2 ») were handed out.
    ///
    /// <para>⚠️ Ordered on <c>Id</c> last, like every other list read in this solution: two markers dropped in
    /// the same millisecond otherwise have no defined order, so one can appear twice across two reads.</para>
    /// </summary>
    Task<IReadOnlyList<PatientFileAnnotation>> GetForFileAsync(Guid fileId, CancellationToken cancellationToken = default);

    Task AddAsync(PatientFileAnnotation annotation, CancellationToken cancellationToken = default);

    Task UpdateAsync(PatientFileAnnotation annotation, CancellationToken cancellationToken = default);

    Task DeleteAsync(PatientFileAnnotation annotation, CancellationToken cancellationToken = default);
}
