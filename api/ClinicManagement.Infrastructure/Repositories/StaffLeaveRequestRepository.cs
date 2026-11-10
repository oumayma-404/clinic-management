using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class StaffLeaveRequestRepository : IStaffLeaveRequestRepository
{
    private readonly ApplicationDbContext _context;

    public StaffLeaveRequestRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<StaffLeaveRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.StaffLeaveRequests.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public async Task<PagedResult<StaffLeaveRequest>> ListAsync(
        Guid clinicId, bool? pendingOnly, StaffLeaveStatus? status, PageRequest? paging,
        CancellationToken cancellationToken = default)
    {
        var query = _context.StaffLeaveRequests.Where(l => l.ClinicId == clinicId);
        if (status is { } only)
            query = query.Where(l => l.Status == only);
        query = pendingOnly switch
        {
            true => query.Where(l => l.Status == StaffLeaveStatus.Pending),
            false => query.Where(l => l.Status != StaffLeaveStatus.Pending),
            null => query,
        };
        IOrderedQueryable<StaffLeaveRequest> ordered = pendingOnly switch
        {
            true => query.OrderBy(l => l.CreatedAt),
            false => query.OrderByDescending(l => l.StartDate),
            null => query
                .OrderBy(l => l.Status == StaffLeaveStatus.Pending ? 0 : 1)
                .ThenByDescending(l => l.StartDate),
        };
        // A unique column last, or OFFSET shows one request twice and skips another.
        ordered = ordered.ThenBy(l => l.Id);

        if (paging is not { } page)
        {
            var all = await ordered.ToListAsync(cancellationToken);
            return PagedResult<StaffLeaveRequest>.Unpaged(all);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await ordered.Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);
        return new PagedResult<StaffLeaveRequest>(items, page.Page, page.PageSize, total);
    }

    public async Task<int> CountPendingAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffLeaveRequests
            .CountAsync(l => l.ClinicId == clinicId && l.Status == StaffLeaveStatus.Pending, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffLeaveRequest>> GetByUserAsync(
        Guid clinicId, string userId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffLeaveRequests
            .Where(l => l.ClinicId == clinicId && l.UserId == userId)
            .OrderByDescending(l => l.StartDate)
            .ThenByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<StaffLeaveRequest?> FindHoldingOverlapAsync(
        Guid clinicId, string userId, DateOnly fromDay, DateOnly toDay, CancellationToken cancellationToken = default)
    {
        return await Touching(clinicId, fromDay, toDay)
            .Where(l => l.UserId == userId
                && (l.Status == StaffLeaveStatus.Pending || l.Status == StaffLeaveStatus.Approved))
            .OrderBy(l => l.StartDate)
            .ThenBy(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffLeaveRequest>> GetApprovedOverlappingAsync(
        Guid clinicId, DateOnly fromDay, DateOnly toDay, CancellationToken cancellationToken = default)
    {
        return await Touching(clinicId, fromDay, toDay)
            .Where(l => l.Status == StaffLeaveStatus.Approved)
            .OrderBy(l => l.StartDate)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffLeaveRequest>> GetHoldingOverlappingAsync(
        Guid clinicId, DateOnly fromDay, DateOnly toDay, CancellationToken cancellationToken = default)
    {
        return await Touching(clinicId, fromDay, toDay)
            .Where(l => l.Status == StaffLeaveStatus.Pending || l.Status == StaffLeaveStatus.Approved)
            .OrderBy(l => l.StartDate)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The one overlap rule: both ends are inclusive days.</summary>
    private IQueryable<StaffLeaveRequest> Touching(Guid clinicId, DateOnly fromDay, DateOnly toDay) =>
        _context.StaffLeaveRequests.Where(l => l.ClinicId == clinicId && l.StartDate <= toDay && fromDay <= l.EndDate);

    public async Task<bool> HasApprovedCoveringAsync(
        Guid clinicId, string userId, DateOnly day, CancellationToken cancellationToken = default)
    {
        return await Touching(clinicId, day, day)
            .AnyAsync(l => l.UserId == userId && l.Status == StaffLeaveStatus.Approved, cancellationToken);
    }

    public async Task AddAsync(StaffLeaveRequest request, CancellationToken cancellationToken = default)
    {
        await _context.StaffLeaveRequests.AddAsync(request, cancellationToken);
    }
}
