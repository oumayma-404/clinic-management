using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>The per-PC copy reports of the camera originals (<c>features/camera-photo-import</c>).</summary>
public interface IPhotoOriginalsCopyRepository
{
    Task<PhotoOriginalsCopy?> GetAsync(Guid clinicId, string heldBy, CancellationToken cancellationToken = default);

    /// <summary>Every PC's report for one cabinet. Reads across the tenant filter: the daily pass runs system-wide.</summary>
    Task<IReadOnlyList<PhotoOriginalsCopy>> ListAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task AddAsync(PhotoOriginalsCopy copy, CancellationToken cancellationToken = default);
}
