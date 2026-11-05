using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class StaffInvitationRepository : IStaffInvitationRepository
{
    private const int PurgeBatchSize = 200;

    private readonly ApplicationDbContext _context;

    public StaffInvitationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<StaffInvitation?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        await _context.StaffInvitations.FirstOrDefaultAsync(i => i.UserId == userId, cancellationToken);

    public async Task<StaffInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        await _context.StaffInvitations.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlySet<string>> GetPendingUserIdsAsync(
        IReadOnlyCollection<string> userIds, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new HashSet<string>();
        }

        var ids = await _context.StaffInvitations
            .Where(i => userIds.Contains(i.UserId) && i.ConsumedAtUtc == null && i.ExpiresAtUtc > nowUtc)
            .Select(i => i.UserId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task AddAsync(StaffInvitation invitation, CancellationToken cancellationToken = default) =>
        await _context.StaffInvitations.AddAsync(invitation, cancellationToken);

    public Task UpdateAsync(StaffInvitation invitation, CancellationToken cancellationToken = default)
    {
        // PasswordResetRequestRepository's guarded form: Version is mapped onto xmin, so a blind Update on a
        // detached instance sends `WHERE xmin = 0` and 409s with nobody at fault.
        if (_context.Entry(invitation).State == EntityState.Detached)
        {
            _context.StaffInvitations.Update(invitation);
        }

        return Task.CompletedTask;
    }

    public async Task<int> PurgeSpentAsync(
        DateTime nowUtc, TimeSpan consumedRetention, CancellationToken cancellationToken = default)
    {
        var consumedBefore = nowUtc - consumedRetention;

        var ids = await _context.StaffInvitations
            .Where(i => (i.ConsumedAtUtc == null && i.ExpiresAtUtc <= nowUtc)
                        || (i.ConsumedAtUtc != null && i.ConsumedAtUtc <= consumedBefore))
            .OrderBy(i => i.ExpiresAtUtc)
            .Select(i => i.Id)
            .Take(PurgeBatchSize)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return 0;
        }

        return await _context.StaffInvitations.Where(i => ids.Contains(i.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
