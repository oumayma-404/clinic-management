using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class DoctorLeaveRepository : IDoctorLeaveRepository
{
    private readonly ApplicationDbContext _context;

    public DoctorLeaveRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DoctorLeave?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.DoctorLeaves.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorLeave>> GetByDoctorAsync(
        Guid clinicId, Guid doctorId, CancellationToken cancellationToken = default)
    {
        return await _context.DoctorLeaves
            .Where(l => l.ClinicId == clinicId && l.DoctorId == doctorId)
            .OrderBy(l => l.StartDate)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorLeave>> GetOverlappingAsync(
        Guid clinicId, DateOnly fromDay, DateOnly toDay, CancellationToken cancellationToken = default)
    {
        return await Touching(clinicId, fromDay, toDay)
            .OrderBy(l => l.StartDate)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<DoctorLeave?> FindOverlappingAsync(
        Guid clinicId,
        Guid doctorId,
        DateOnly fromDay,
        DateOnly toDay,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        return await Touching(clinicId, fromDay, toDay)
            .Where(l => l.DoctorId == doctorId && (excludeId == null || l.Id != excludeId))
            .OrderBy(l => l.StartDate)
            .ThenBy(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The one overlap rule: both ends are inclusive days.</summary>
    private IQueryable<DoctorLeave> Touching(Guid clinicId, DateOnly fromDay, DateOnly toDay) =>
        _context.DoctorLeaves.Where(l => l.ClinicId == clinicId && l.StartDate <= toDay && fromDay <= l.EndDate);

    public async Task AddAsync(DoctorLeave leave, CancellationToken cancellationToken = default)
    {
        await _context.DoctorLeaves.AddAsync(leave, cancellationToken);
    }

    public void Remove(DoctorLeave leave)
    {
        _context.DoctorLeaves.Remove(leave);
    }
}
