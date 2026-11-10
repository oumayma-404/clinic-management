using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class StaffLeaveAllowanceRepository : IStaffLeaveAllowanceRepository
{
    private readonly ApplicationDbContext _context;

    public StaffLeaveAllowanceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<StaffLeaveAllowanceEntry>> GetForUserAsync(
        Guid clinicId, string userId, CancellationToken cancellationToken = default) =>
        await _context.StaffLeaveAllowanceEntries
            .AsNoTracking()
            .Where(e => e.ClinicId == clinicId && e.UserId == userId)
            .OrderBy(e => e.OnDay).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StaffLeaveAllowanceEntry>> GetForClinicAsync(
        Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.StaffLeaveAllowanceEntries
            .AsNoTracking()
            .Where(e => e.ClinicId == clinicId)
            .OrderBy(e => e.OnDay).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

    public async Task<StaffLeaveAllowanceEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.StaffLeaveAllowanceEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task AddAsync(StaffLeaveAllowanceEntry entry, CancellationToken cancellationToken = default) =>
        await _context.StaffLeaveAllowanceEntries.AddAsync(entry, cancellationToken);
}
