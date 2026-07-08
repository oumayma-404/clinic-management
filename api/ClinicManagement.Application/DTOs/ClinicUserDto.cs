namespace ClinicManagement.Application.DTOs;

/// <summary>
/// A clinic user as seen on the admin user-management screen: identity, role and account
/// status (AC-5.1). Extends the basic <see cref="UserDto"/> shape with local-account state.
/// </summary>
public class ClinicUserDto
{
    public string Id { get; set; } = string.Empty;
    public Guid ClinicId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
