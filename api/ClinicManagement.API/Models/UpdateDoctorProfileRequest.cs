using Microsoft.AspNetCore.Http;

namespace ClinicManagement.API.Models;

/// <summary>
/// Multipart form for a doctor-profile update (FR-2.5 / FR-3.1). The cachet is an optional image upload;
/// <see cref="RemoveCachet"/> clears the existing one. Ordre number is a plain text field.
/// </summary>
public class UpdateDoctorProfileRequest
{
    public string? OrdreNumberCnomdt { get; set; }
    public IFormFile? Cachet { get; set; }
    public bool RemoveCachet { get; set; }
}
