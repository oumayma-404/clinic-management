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
}
