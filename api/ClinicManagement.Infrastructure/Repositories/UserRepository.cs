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

    public async Task<User?> GetByAuth0SubAsync(string auth0Sub, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.Clinic)
            .FirstOrDefaultAsync(u => u.Id == auth0Sub, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return await _context.Users
            .Include(u => u.Clinic)
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


