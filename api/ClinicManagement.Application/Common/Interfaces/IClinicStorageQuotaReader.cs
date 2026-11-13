namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// One cabinet's own storage ceiling, if the vendor set one (<c>Clinic.StorageQuotaBytes</c>, phase 3 of
/// <c>features/camera-photo-import</c>). A read of one column rather than the whole <c>Clinic</c> aggregate, because it
/// runs on every upload.
/// </summary>
public interface IClinicStorageQuotaReader
{
    Task<long?> GetOverrideAsync(Guid clinicId, CancellationToken cancellationToken = default);
}
