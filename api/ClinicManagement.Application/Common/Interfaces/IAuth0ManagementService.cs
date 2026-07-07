namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Service for interacting with Auth0 Management API
/// </summary>
public interface IAuth0ManagementService
{
    /// <summary>
    /// Updates user's app_metadata with clinic_id and role
    /// </summary>
    Task UpdateUserMetadataAsync(string userId, Guid clinicId, string role, CancellationToken cancellationToken = default);
}

