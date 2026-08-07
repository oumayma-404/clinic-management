using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
namespace ClinicManagement.Infrastructure.Repositories;

public class LabWorkOrderRepository : ILabWorkOrderRepository
{
    private readonly ApplicationDbContext _context;

    public LabWorkOrderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<LabWorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.LabWorkOrders
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<PagedResult<LabWorkOrder>> GetByClinicIdAsync(
        Guid clinicId,
        LabOrderStatus? status = null,
        Guid? patientId = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.LabWorkOrders
            .Include(o => o.Patient)
            .Where(o => o.ClinicId == clinicId);

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        // The patient filter moved in here from the handler, which used to branch to GetByPatientIdAsync and
        // then re-apply the status filter in memory. Two predicates for one list is how the filtered and
        // unfiltered views drift — and only one of the two branches could ever have been paged.
        if (patientId.HasValue)
        {
            query = query.Where(o => o.PatientId == patientId.Value);
        }

        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(o =>
                EF.Functions.ILike(SqlSearch.Unaccent(o.Prosthetist)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(o.WorkDescription)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(o.Notes)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(o.Patient!.FirstName + " " + o.Patient.LastName)!, pattern, SqlSearch.EscapeString));
        }

        return await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<int> CountOverdueAsync(Guid clinicId, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        // « En retard » = still at the lab past the date it was expected back. An order with no ExpectedDate has
        // nothing to be late against and is deliberately not counted — guessing a default would invent a deadline
        // the clinic never agreed with the prothésiste. Received/Fitted are already back, so only Sent qualifies;
        // InProgress is excluded on the same reading (the lab has acknowledged it and is working).
        return await _context.LabWorkOrders
            .Where(o => o.ClinicId == clinicId
                        && o.Status == LabOrderStatus.Sent
                        && o.ExpectedDate != null
                        && o.ExpectedDate < asOfUtc)
            .CountAsync(cancellationToken);
    }

    public async Task<IEnumerable<LabWorkOrder>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.LabWorkOrders
            .Include(o => o.Patient)
            .Where(o => o.PatientId == patientId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<LabWorkOrder> AddAsync(LabWorkOrder order, CancellationToken cancellationToken = default)
    {
        await _context.LabWorkOrders.AddAsync(order, cancellationToken);
        return order;
    }

    public Task UpdateAsync(LabWorkOrder order, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(order);
        if (entry.State == EntityState.Detached)
        {
            _context.LabWorkOrders.Update(order);
        }
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await GetByIdAsync(id, cancellationToken);
        if (order != null)
        {
            _context.LabWorkOrders.Remove(order);
        }
    }
}
