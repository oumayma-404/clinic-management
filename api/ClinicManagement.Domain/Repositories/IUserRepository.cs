using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// The clinic's staff list, ordered by name (it had no ordering, which paging cannot tolerate).
    /// <paramref name="searchTerm"/> is matched in SQL over full name and email; <paramref name="paging"/> of
    /// null returns every member — the "is there another active admin?" guard depends on seeing all of them.
    /// </summary>
    Task<PagedResult<User>> GetByClinicIdAsync(
        Guid clinicId,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default);
    Task<User?> GetByAuth0SubAsync(string auth0Sub, CancellationToken cancellationToken = default);
    /// <summary>Looks up a local (password-backed) account by email. Used for Local-mode login.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    /// <summary>True if any user exists. Used to close first-run setup once the first admin is created.</summary>
    Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// How many of the clinic's accounts are waiting for an admin to activate them (I5) — inactive and never
    /// logged in. A count rather than a filter on the list read, and deliberately **not** narrowed by the search
    /// term: it is the figure above the table, describing the whole clinic, so an admin who filtered for one name
    /// must still see that someone is waiting. Counting the loaded page instead would read « 0 en attente »
    /// whenever the pending colleagues sort onto page 2.
    /// </summary>
    Task<int> CountPendingActivationAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task AddAsync(User entity, CancellationToken cancellationToken = default);
    void Update(User entity);
    void Remove(User entity);
}

