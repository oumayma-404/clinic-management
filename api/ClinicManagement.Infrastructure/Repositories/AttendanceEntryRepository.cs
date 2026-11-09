using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class AttendanceEntryRepository : IAttendanceEntryRepository
{
    private readonly ApplicationDbContext _context;

    public AttendanceEntryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AttendanceEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceEntries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<AttendanceEntry?> FindLatestOpenAsync(
        Guid clinicId, string userId, CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceEntries
            .Where(e => e.ClinicId == clinicId && e.UserId == userId && e.DepartureUtc == null)
            .OrderByDescending(e => e.ArrivalUtc)
            .ThenBy(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DateOnly?> GetFirstDayAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        await _context.AttendanceEntries
            .AsNoTracking()
            .Where(e => e.ClinicId == clinicId)
            .OrderBy(e => e.LocalDay)
            .Select(e => (DateOnly?)e.LocalDay)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AttendanceEntry>> ListBetweenAsync(
        Guid clinicId, DateOnly fromDay, DateOnly toDay, string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AttendanceEntries
            .Where(e => e.ClinicId == clinicId && e.LocalDay >= fromDay && e.LocalDay <= toDay);
        if (userId is not null)
            query = query.Where(e => e.UserId == userId);
        return await query
            .OrderBy(e => e.LocalDay)
            .ThenBy(e => e.ArrivalUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasLineOnAsync(
        Guid clinicId, string userId, DateOnly day, CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceEntries
            .AnyAsync(e => e.ClinicId == clinicId && e.UserId == userId && e.LocalDay == day, cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceEntry>> ListOpenOnAsync(
        Guid clinicId, DateOnly day, CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceEntries
            .AsNoTracking()
            .Where(e => e.ClinicId == clinicId && e.LocalDay == day && e.DepartureUtc == null)
            .OrderBy(e => e.ArrivalUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceEntry>> ListOpenBeforeAsync(
        Guid clinicId, string userId, DateOnly day, CancellationToken cancellationToken = default)
    {
        return await _context.AttendanceEntries
            .Where(e => e.ClinicId == clinicId && e.UserId == userId && e.LocalDay < day && e.DepartureUtc == null)
            .OrderBy(e => e.LocalDay)
            .ThenBy(e => e.ArrivalUtc)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(AttendanceEntry entry, CancellationToken cancellationToken = default)
    {
        await _context.AttendanceEntries.AddAsync(entry, cancellationToken);
    }
}
