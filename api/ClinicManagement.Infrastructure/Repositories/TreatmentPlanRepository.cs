using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class TreatmentPlanRepository : ITreatmentPlanRepository
{
    private readonly ApplicationDbContext _context;

    public TreatmentPlanRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TreatmentPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TreatmentPlans
            .Include(p => p.Items)
            .Include(p => p.Installments)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<TreatmentPlan>> GetFilteredAsync(
        Guid clinicId,
        Guid? patientId = null,
        TreatmentPlanStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TreatmentPlans
            .Include(p => p.Items)
            .Include(p => p.Installments)
            .Where(p => p.ClinicId == clinicId);

        if (patientId.HasValue)
        {
            query = query.Where(p => p.PatientId == patientId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(p => p.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(p => p.CreatedAt <= to.Value);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetMaxSequenceForYearAsync(Guid clinicId, int year, CancellationToken cancellationToken = default)
    {
        var prefix = $"{year}-";

        var numbers = await _context.TreatmentPlans
            .Where(p => p.ClinicId == clinicId && p.Number != null && p.Number.StartsWith(prefix))
            .Select(p => p.Number!)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var dashIndex = number.LastIndexOf('-');
            if (dashIndex >= 0 && int.TryParse(number[(dashIndex + 1)..], out var sequence) && sequence > max)
            {
                max = sequence;
            }
        }

        return max;
    }

    public async Task<TreatmentPlan> AddAsync(TreatmentPlan plan, CancellationToken cancellationToken = default)
    {
        await _context.TreatmentPlans.AddAsync(plan, cancellationToken);
        return plan;
    }

    public Task UpdateAsync(TreatmentPlan plan, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(plan);
        if (entry.State == EntityState.Detached)
        {
            _context.TreatmentPlans.Update(plan);
        }
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _context.TreatmentPlans
            .Include(p => p.Items)
            .Include(p => p.Installments)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (plan != null)
        {
            _context.TreatmentPlans.Remove(plan);
        }
    }
}
