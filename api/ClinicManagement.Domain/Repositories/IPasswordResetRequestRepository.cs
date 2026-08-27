using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Live and recently-spent password-reset requests. <b>Not clinic-scoped</b> — <see cref="PasswordResetRequest"/>
/// has no <c>ClinicId</c>, so no read here takes the tenant query filter and none needs to. That is deliberate and
/// load-bearing: both callers are anonymous endpoints with no scope established, and a filtered read under an
/// <c>Unset</c> scope returns zero rows with no error.
/// </summary>
public interface IPasswordResetRequestRepository
{
    /// <summary>
    /// The request belonging to this account, whatever its state — so the caller can re-arm the existing row
    /// instead of creating a second live token, and can apply the per-account cooldown before mailing again.
    /// A <b>consumed</b> row is returned too: it is the row that gets re-armed on the next request.
    /// </summary>
    Task<PasswordResetRequest?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The row whose token hashes to <paramref name="tokenHash"/>, or null. The hash is what is stored, so this is
    /// the only lookup the completion step has — the raw token is in the person's email and nowhere else.
    /// </summary>
    Task<PasswordResetRequest?> GetByTokenHashAsync(
        string tokenHash, CancellationToken cancellationToken = default);

    Task AddAsync(PasswordResetRequest request, CancellationToken cancellationToken = default);

    Task UpdateAsync(PasswordResetRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes rows that can no longer do anything — expired and unconsumed, or consumed longer ago than
    /// <paramref name="consumedRetention"/> — and returns how many. Called opportunistically from the request path
    /// rather than from a background job, for the reason <see cref="IClinicSignupRepository.PurgeSpentAsync"/>
    /// states: the table only grows when somebody asks for a reset, so the write that grows it is the moment to
    /// trim it.
    ///
    /// <para>⚠️ It commits on its own and is <b>bounded per call</b> — staging these deletes on the caller's
    /// <c>SaveChangesAsync</c> turns a concurrent purge of the same rows into a 409 on a request that was
    /// perfectly valid. The signup repository's own remarks record that defect in full.</para>
    /// </summary>
    Task<int> PurgeSpentAsync(
        DateTime nowUtc, TimeSpan consumedRetention, CancellationToken cancellationToken = default);
}
