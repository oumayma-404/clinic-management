using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Persistence for one user's dashboard layout choices (1:1 with the user; keyed by user id).
/// Mutations only stage changes — the caller commits via <c>IUnitOfWork</c>.
/// </summary>
public interface IUserDashboardPreferenceRepository
{
    /// <summary>
    /// The user's row, or <c>null</c> when they have never customised the dashboard. <c>null</c> is a normal
    /// answer, not a missing-data problem: it means "nothing hidden", which is what a fresh account should see.
    /// </summary>
    Task<UserDashboardPreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task AddAsync(UserDashboardPreference preference, CancellationToken cancellationToken = default);

    Task UpdateAsync(UserDashboardPreference preference, CancellationToken cancellationToken = default);
}
