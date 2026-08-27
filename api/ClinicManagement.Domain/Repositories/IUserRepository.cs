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

    /// <summary>
    /// How many staff accounts a clinic has and when any of them last signed in — the two figures the vendor
    /// console's counter pass reports per cabinet (<c>platform-console</c> AC-2.1).
    ///
    /// <para>One aggregate rather than <c>GetByClinicIdAsync</c> with no paging: the pass runs for every cabinet
    /// on every run and needs two scalars, not every colleague's row. Both are also the answer to « is anyone
    /// still using this? », which is the question the portfolio exists to ask.</para>
    /// </summary>
    Task<ClinicStaffSummary> GetStaffSummaryAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Who to contact at a cabinet — the administrator the vendor console's detail names (<c>platform-console</c>
    /// AC-3.3). Null where the cabinet has no admin account at all.
    ///
    /// <para>⚠️ <b>Active admins win, then the oldest.</b> A practice accumulates admins (the founder, a partner,
    /// a departed manager who was switched off), and the one the vendor should ring is someone who can still sign
    /// in — with the founder ahead of a later addition. Deterministic to the end (<c>ThenBy(Id)</c>), so the
    /// detail does not name a different person on two consecutive loads.</para>
    ///
    /// <para>A projection rather than a <c>User</c>: this read exists to put two strings on a screen, and
    /// returning the aggregate would hand a cross-cabinet surface the whole account row including its password
    /// hash and lockout state.</para>
    /// </summary>
    Task<ClinicAdminContact?> GetPrimaryAdminContactAsync(
        Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same answer as <see cref="GetPrimaryAdminContactAsync"/> for a whole page of cabinets, keyed by clinic —
    /// what the portfolio list shows in its « Administrateur » column. A cabinet with no admin account is simply
    /// absent from the dictionary.
    ///
    /// <para>⚠️ <b>This is the primary of the two, and the single-cabinet read delegates to it.</b> « Which admin is
    /// the cabinet's contact? » is a precedence rule (active first, then the founder, then deterministic), and a
    /// second expression of it would drift into the list naming one person and the fiche naming another — both
    /// screens looking right on their own, which is the hardest kind of discrepancy to notice.</para>
    ///
    /// <para>Batched because the alternative is a query per row of every page load, on the read a vendor opens
    /// first.</para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ClinicAdminContact>> GetPrimaryAdminContactsAsync(
        IEnumerable<Guid> clinicIds, CancellationToken cancellationToken = default);

    Task AddAsync(User entity, CancellationToken cancellationToken = default);
    void Update(User entity);
    void Remove(User entity);
}

/// <summary>
/// A cabinet's administrator, reduced to what the vendor may see: a name and an address to reach them at.
///
/// <para><see cref="IsActive"/> travels with it because « l'administrateur est désactivé » is the answer to a
/// support call, and a name shown with no such marker reads as somebody who can be reached.</para>
/// </summary>
public sealed record ClinicAdminContact(string? FullName, string? Email, bool IsActive);

/// <summary>A clinic's staff, reduced to the two figures the portfolio reports. <paramref name="LastLoginAt"/> is
/// null where nobody has ever signed in — which for a cabinet created weeks ago is the loudest churn signal there
/// is, and is why it is nullable rather than defaulted to the creation date.</summary>
public sealed record ClinicStaffSummary(int Count, DateTime? LastLoginAt);

