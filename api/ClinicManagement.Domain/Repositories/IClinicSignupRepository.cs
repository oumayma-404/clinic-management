using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// Pending clinic signups. <b>Not clinic-scoped</b> — a signup exists precisely because no clinic does — so no
/// read here takes the tenant query filter and none needs to.
/// </summary>
public interface IClinicSignupRepository
{
    /// <summary>
    /// The signup for this address, whatever its state. Used on the signup path so a second attempt re-arms the
    /// existing row instead of creating a second live token (AC-6); a <b>consumed</b> row is returned too, so the
    /// caller can tell « already provisioned » from « never asked ».
    /// </summary>
    Task<ClinicSignup?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// The row whose token hashes to <paramref name="tokenHash"/>, or null. The hash is what is stored, so this
    /// is the only lookup verification has — the raw token is in the visitor's email and nowhere else.
    /// </summary>
    Task<ClinicSignup?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task AddAsync(ClinicSignup signup, CancellationToken cancellationToken = default);

    Task UpdateAsync(ClinicSignup signup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes rows that can no longer do anything — expired and unconsumed, or consumed longer ago than
    /// <paramref name="consumedRetention"/> — and returns how many. Called opportunistically from the signup path
    /// (AC-7) rather than from a background job: the table only grows when somebody signs up, so the write that
    /// grows it is exactly the moment to trim it, and a whole recurring job for one small table is machinery
    /// nobody needs.
    ///
    /// <para>⚠️ It commits on its own and is <b>bounded per call</b>, deliberately — see the implementation for
    /// the 409 that staging these deletes on the caller's <c>SaveChangesAsync</c> produced.</para>
    /// </summary>
    Task<int> PurgeSpentAsync(
        DateTime nowUtc, TimeSpan consumedRetention, CancellationToken cancellationToken = default);
}
