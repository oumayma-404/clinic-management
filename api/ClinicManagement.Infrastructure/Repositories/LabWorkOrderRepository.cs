using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.LabOrders;
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
        Guid? supplierId = null,
        bool orderByExpectedDate = false,
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

        // « Quels bons sont chez ce labo ? » had no answer: the page offered a stage filter and nothing else, so
        // the fiche fournisseur — the record that exists to stop the practice relying on a retyped name — could
        // not be used to narrow the list it appears on.
        if (supplierId.HasValue)
        {
            query = query.Where(o => o.SupplierId == supplierId.Value);
        }

        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(o =>
                EF.Functions.ILike(SqlSearch.Unaccent(o.Prosthetist)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(o.WorkDescription)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(o.Notes)!, pattern, SqlSearch.EscapeString) ||
                // The LINKED fiche's nom, not only the free-text `Prosthetist` typed onto the bon. Three bons
                // filed under « Laboratoire Ben Aissa » returned « aucun bon » for that name: they were findable
                // only through the retyped prothésiste string, which is the field the fiche exists to replace.
                // A subquery, not a navigation: `LabWorkOrder` deliberately holds only `SupplierId` — the bon
                // prints the name it was raised with, and the DTO resolves the fiche through a batched read.
                _context.Suppliers.Any(sp =>
                    sp.Id == o.SupplierId &&
                    EF.Functions.ILike(SqlSearch.Unaccent(sp.Name)!, pattern, SqlSearch.EscapeString)) ||
                // BOTH name orders: the app renders « Nom Prénom » — see `PatientRepository.ApplySearch`.
                EF.Functions.ILike(SqlSearch.Unaccent(o.Patient!.FirstName + " " + o.Patient.LastName)!, pattern, SqlSearch.EscapeString) ||
                EF.Functions.ILike(SqlSearch.Unaccent(o.Patient!.LastName + " " + o.Patient.FirstName)!, pattern, SqlSearch.EscapeString));
        }

        // Nulls last in both orders: a bon with no date agreed is not "due first", and `ThenBy(Id)` is the unique
        // tie-break every paged read in this solution carries — `OFFSET` over a non-unique sort shows one row
        // twice and skips another, which reads as « un bon a disparu ».
        query = orderByExpectedDate
            ? query.OrderBy(o => o.ExpectedDate == null).ThenBy(o => o.ExpectedDate).ThenBy(o => o.Id)
            : query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id);

        return await query.ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<int> CountOverdueAsync(Guid clinicId, DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        // The rule itself lives in `LabOrderOverdue` — the badge on /lab-orders reads the same expression, which
        // is what stops the card's N and the rows wearing a badge from being two different N.
        return await _context.LabWorkOrders
            .Where(o => o.ClinicId == clinicId)
            .Where(LabOrderOverdue.Predicate(LabOrderOverdue.CutoffUtc(asOfUtc)))
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
