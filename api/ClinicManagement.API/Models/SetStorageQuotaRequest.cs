namespace ClinicManagement.API.Models;

/// <summary>
/// A cabinet's storage ceiling from the vendor console (<c>features/camera-photo-import</c>, phase 3).
/// <c>QuotaBytes: null</c> returns the cabinet to the deployment's default.
/// </summary>
public class SetStorageQuotaRequest
{
    public long? QuotaBytes { get; set; }

    /// <summary>Mandatory; the handler refuses a blank one in French.</summary>
    public string? Reason { get; set; }
}
