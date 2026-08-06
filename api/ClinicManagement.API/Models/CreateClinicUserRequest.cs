using ClinicManagement.Application.DTOs;

namespace ClinicManagement.API.Models;

/// <summary>
/// Body of <c>POST /api/users</c> — an admin creating a colleague's account (<c>multi-tenant-cloud</c> US-3).
/// No password field: the server mints a one-time one and returns it once.
/// </summary>
public class CreateClinicUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>« admin », « doctor » or « secretary » (case-insensitive). Validated against the closed set.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The practitioner behind a <c>doctor</c> account — prénom, nom, spécialité and an optional phone.
    /// <b>Required for that role</b>, ignored for the other two, mirroring <c>POST /api/clinics/join</c>.
    /// Without it the account gets no <c>Doctor</c> record, and its documents print with no practitioner identity.
    /// </summary>
    public DoctorPersonalInfoDto? DoctorInfo { get; set; }
}
