using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The colleague invitations (<c>guided-first-days</c> C). <see cref="StaffInvitation"/> carries no <c>ClinicId</c>
/// — it is reached through its user — so a read by token runs fine in the anonymous (unscoped) request that opens the
/// link.
/// </summary>
public interface IStaffInvitationRepository
{
    Task<StaffInvitation?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    Task<StaffInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Of these accounts, the ones whose invitation is still waiting to be used (« Invitation envoyée »).</summary>
    Task<IReadOnlySet<string>> GetPendingUserIdsAsync(
        IReadOnlyCollection<string> userIds, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task AddAsync(StaffInvitation invitation, CancellationToken cancellationToken = default);

    Task UpdateAsync(StaffInvitation invitation, CancellationToken cancellationToken = default);

    /// <summary>Deletes used invitations past <paramref name="consumedRetention"/> and expired unused ones; returns how many.</summary>
    Task<int> PurgeSpentAsync(DateTime nowUtc, TimeSpan consumedRetention, CancellationToken cancellationToken = default);
}
