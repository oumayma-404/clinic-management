namespace ClinicManagement.API.Models;

/// <summary>Body of <c>PUT /api/users/{id}/role</c>. Validated against the closed set in the handler.</summary>
public class SetUserRoleRequest
{
    /// <summary>« admin », « doctor » or « secretary » (case-insensitive).</summary>
    public string Role { get; set; } = string.Empty;
}
