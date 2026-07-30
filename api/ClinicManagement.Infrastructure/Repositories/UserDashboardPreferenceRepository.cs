using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class UserDashboardPreferenceRepository : IUserDashboardPreferenceRepository
{
    private readonly ApplicationDbContext _context;

    public UserDashboardPreferenceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    // Keyed by user id (shared primary key). Carries no clinic filter — the row is scoped by the user it belongs
    // to, and a user belongs to exactly one clinic (the NotificationRead precedent).
    public async Task<UserDashboardPreference?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserDashboardPreferences
            .FirstOrDefaultAsync(p => p.Id == userId, cancellationToken);
    }

    public async Task AddAsync(UserDashboardPreference preference, CancellationToken cancellationToken = default)
    {
        await _context.UserDashboardPreferences.AddAsync(preference, cancellationToken);
    }

    public Task UpdateAsync(UserDashboardPreference preference, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance — the same reasoning documented on
        // ClinicReminderSettingsRepository.UpdateAsync. On the normal path the handler loaded the row through
        // this DbContext, so it is tracked and change tracking holds the real original xmin token; calling
        // Update() on a tracked entity re-marks every property modified, and on a never-loaded detached one the
        // token reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(preference);
        if (entry.State == EntityState.Detached)
        {
            _context.UserDashboardPreferences.Update(preference);
        }
        return Task.CompletedTask;
    }
}
