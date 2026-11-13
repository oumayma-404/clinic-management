using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>The cabinet's paired cameras (<c>features/camera-photo-import</c>).</summary>
public interface IClinicCameraRepository
{
    Task<ClinicCamera?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The one camera a fingerprint names in this cabinet. <paramref name="clinicId"/> is a real predicate, not
    /// documentation: two cabinets owning the same model must never share a registry entry.
    /// </summary>
    Task<ClinicCamera?> GetByFingerprintAsync(Guid clinicId, string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>Every camera of the cabinet, by label then id — a handful of rows, never paged.</summary>
    Task<IReadOnlyList<ClinicCamera>> ListAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task AddAsync(ClinicCamera camera, CancellationToken cancellationToken = default);

    Task DeleteAsync(ClinicCamera camera, CancellationToken cancellationToken = default);
}
