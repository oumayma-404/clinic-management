using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// No-op <see cref="IAuth0ManagementService"/> used in Local (offline) mode, where there is
/// no Auth0 tenant to push clinic/role metadata into. Keeps clinic-scoped handlers that depend
/// on this service working unchanged (FR-A5).
/// </summary>
public class NoOpAuth0ManagementService : IAuth0ManagementService
{
    public Task UpdateUserMetadataAsync(string userId, Guid clinicId, string role, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
