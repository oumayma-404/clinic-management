using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// The console identity population's repository (FR-1). Deliberately narrow: two lookups and an insert, because
/// there is no console screen that lists, creates or deactivates an account — those are the bootstrap verb's job
/// (AC-8.5), and a read that nothing needs is a read a future screen would be tempted to build on.
/// </summary>
public interface IPlatformAccountRepository
{
    /// <summary>
    /// By address. ⚠️ Normalises through <c>EmailNormalization</c> inside the implementation rather than trusting
    /// the caller: the unique index is on the lowered form, so a caller that forgot would silently fail to find
    /// the account it was about to create a duplicate of.
    /// </summary>
    Task<PlatformAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// By id, with the recovery codes loaded — every caller that has an id either checks a code or is about to
    /// invalidate the set, so the collection is not optional and a lazy path would be N+1 on the sign-in.
    /// </summary>
    Task<PlatformAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The state check on every console request (AC-1.6), without the recovery codes: the middleware needs
    /// <c>IsActive</c> and <c>TokenVersion</c> and nothing else, and pulling the child collection on every call
    /// would make the cheapest per-request lookup in the console the most expensive.
    /// </summary>
    Task<PlatformAccount?> GetForStateCheckAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(PlatformAccount account, CancellationToken cancellationToken = default);
}
