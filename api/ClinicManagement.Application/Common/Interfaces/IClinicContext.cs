namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Service to extract clinic and user information from JWT claims
/// </summary>
public interface IClinicContext
{
    /// <summary>
    /// Gets the current user's clinic ID from JWT claims
    /// </summary>
    Guid? GetClinicId();

    /// <summary>
    /// Gets the current user's role from JWT claims
    /// </summary>
    string? GetUserRole();

    /// <summary>
    /// Gets the current user's Auth0 sub (user ID)
    /// </summary>
    string? GetUserId();

    /// <summary>
    /// Gets the current user's email from JWT claims
    /// </summary>
    string? GetUserEmail();

    /// <summary>
    /// Checks if the current user belongs to the specified clinic
    /// </summary>
    bool BelongsToClinic(Guid clinicId);

    /// <summary>
    /// Throws ForbiddenAccessException if user doesn't belong to the clinic
    /// </summary>
    void EnsureClinicAccess(Guid clinicId);
}



