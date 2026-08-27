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

    /// <summary>
    /// This account has never been able to log in and is waiting for an admin's approval (I5) — as opposed to
    /// having been switched off after use. Both are <c>!IsActive</c>; only this one is somebody's first day.
    /// The row's badge reads « En attente d'activation » rather than « Désactivé » on the strength of it.
    /// </summary>
    public bool IsPendingActivation { get; set; }

    public bool MustChangePassword { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Round-tripped by the role / activation actions so a concurrent change is a 409, not an overwrite.</summary>
    public uint Version { get; set; }

    public DateTime CreatedAt { get; set; }
}
