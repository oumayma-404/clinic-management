using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The entitlement and its ledger. Mutations only stage; the Application <c>IUnitOfWork</c> commits — which is what
/// lets provisioning put a cabinet and its entitlement in <b>one</b> save (FR-4's « one indivisible operation »).
/// </summary>
public interface IClinicSubscriptionRepository
{
    /// <summary>The cabinet's entitlement, or null — which the gate reports as <c>subscription_missing</c> (EC-6).</summary>
    Task<ClinicSubscription?> GetByClinicAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The cabinet's <b>whole</b> ledger, oldest first.
    ///
    /// <para>⚠️ Deliberately not paged. Every caller either folds it — and a fold over a page is not a fold
    /// (<c>ClinicSubscription.RecomputeFrom</c>) — or is the history screen, which folds the whole ledger for its
    /// derived « période couverte » and then cuts a page in memory with <c>PagedResult.FromSource</c>.</para>
    /// </summary>
    Task<IReadOnlyList<SubscriptionPeriod>> GetEntriesAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    Task AddAsync(ClinicSubscription subscription, CancellationToken cancellationToken = default);

    Task AddEntryAsync(SubscriptionPeriod entry, CancellationToken cancellationToken = default);

    Task UpdateAsync(ClinicSubscription subscription, CancellationToken cancellationToken = default);
}
