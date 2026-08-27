using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.Clinic)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.Clinic)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<User>> GetByClinicIdAsync(
        Guid clinicId,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Users
            .Include(u => u.Clinic)
            .Where(u => u.ClinicId == clinicId);

        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(u =>
                EF.Functions.ILike(SqlSearch.Unaccent(u.FullName)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(u.Email)!, pattern, SqlSearch.EscapeString));
        }

        // The list had no ordering at all — fine while it returned every row and the client sorted, but an
        // unordered paged read is the one thing paging cannot tolerate. Name first, then the id: `User.Id` is a
        // string (the Auth0 `sub` or `local|{guid}`) and still unique, so it settles ties.
        return await query
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    /// <summary>
    /// The predicate mirrors <c>User.IsPendingActivation</c>. It is repeated here rather than reused because the
    /// entity's version is a C# computed property EF cannot translate — so the two must be read as one rule:
    /// changing what "pending" means changes both, and the badge on the row and the count above it would
    /// otherwise disagree on the same screen.
    /// </summary>
    public async Task<int> CountPendingActivationAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Where(u => u.ClinicId == clinicId && !u.IsActive && u.LastLoginAt == null)
            .CountAsync(cancellationToken);
    }

    public async Task<ClinicStaffSummary> GetStaffSummaryAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Every account of the clinic, active or not: a suspended colleague is still a seat the practice has, and
        // the vendor's question is « how big is this cabinet? », not « who may sign in today? ».
        var summary = await _context.Users
            .Where(u => u.ClinicId == clinicId)
            .GroupBy(u => 1)
            .Select(g => new { Count = g.Count(), LastLoginAt = g.Max(u => u.LastLoginAt) })
            .FirstOrDefaultAsync(cancellationToken);

        return summary is null
            ? new ClinicStaffSummary(0, null)
            : new ClinicStaffSummary(summary.Count, summary.LastLoginAt);
    }

    // Delegates rather than repeating the precedence: one cabinet is the one-element case of a page, and a clinic's
    // admins are a handful of rows.
    public async Task<ClinicAdminContact?> GetPrimaryAdminContactAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        var contacts = await GetPrimaryAdminContactsAsync([clinicId], cancellationToken);
        return contacts.TryGetValue(clinicId, out var contact) ? contact : null;
    }

    public async Task<IReadOnlyDictionary<Guid, ClinicAdminContact>> GetPrimaryAdminContactsAsync(
        IEnumerable<Guid> clinicIds, CancellationToken cancellationToken = default)
    {
        var ids = clinicIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ClinicAdminContact>();
        }

        // Active first, then the oldest account — the founder ahead of a later addition — and Id last so two
        // consecutive loads cannot name two different people. Ordered in SQL, so the pick below is « the first of
        // this cabinet's rows » rather than a second precedence rule.
        var admins = await _context.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.ClinicId) && u.Role == User.RoleAdmin)
            .OrderByDescending(u => u.IsActive)
            .ThenBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Select(u => new { u.ClinicId, u.FullName, u.Email, u.IsActive })
            .ToListAsync(cancellationToken);

        return admins
            .GroupBy(a => a.ClinicId)
            .ToDictionary(
                group => group.Key,
                group => new ClinicAdminContact(group.First().FullName, group.First().Email, group.First().IsActive));
    }

    /// <summary>
    /// The signed-in caller's own account, by the subject their token carries.
    ///
    /// <para>⚠️ <b><c>RecoveryCodes</c> is included, and every second-factor path depends on it.</b>
    /// <see cref="User.RecoveryCodes"/> projects a private backing list, so with the collection left unloaded it
    /// is not stale — it is <i>empty</i>, and every question asked of it answers as if the account held no codes.
    /// That is silent in all four directions: « Sécurité » reports « 0 code inutilisé » over eight live codes,
    /// <c>ReplaceRecoveryCodes</c>' <c>Clear()</c> revokes nothing so a regeneration <i>adds</i> eight rather
    /// than replacing, <c>DisableTotp</c> leaves spendable rows behind an un-enrolled factor, and
    /// <c>ConsumeRecoveryCode</c> matches nothing so the one way back a user can take alone refuses every code
    /// they own. Nothing throws in any of those cases.
    /// <c>RecoveryCodeLoadingCoverageTests</c> holds this read and <see cref="GetByEmailAsync"/> to it.</para>
    /// </summary>
    public async Task<User?> GetByAuth0SubAsync(string auth0Sub, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.Clinic)
            .Include(u => u.RecoveryCodes)
            .FirstOrDefaultAsync(u => u.Id == auth0Sub, cancellationToken);
    }

    /// <summary>
    /// Looks up a local (password-backed) account by email — the sign-in reads.
    ///
    /// <para>⚠️ <b><c>RecoveryCodes</c> is included</b> for the reason <see cref="GetByAuth0SubAsync"/> states:
    /// this is the read behind <c>RedeemRecoveryCodeCommand</c>, and without the collection that command refuses
    /// every code in the table. At most <c>CountPerEnrolment</c> rows per account travel with it, which is what
    /// makes carrying them on the ordinary login path affordable.</para>
    /// </summary>
    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = EmailNormalization.Normalize(email);
        return await _context.Users
            .Include(u => u.Clinic)
            .Include(u => u.RecoveryCodes)
            .FirstOrDefaultAsync(
                u => u.PasswordHash != null && u.Email != null && u.Email.ToLower() == normalized,
                cancellationToken);
    }

    public async Task<bool> AnyUserExistsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Users.AnyAsync(cancellationToken);
    }

    public async Task AddAsync(User entity, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(entity, cancellationToken);
    }

    public void Update(User entity)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            _context.Users.Update(entity);
        }
    }

    public void Remove(User entity)
    {
        _context.Users.Remove(entity);
    }
}


