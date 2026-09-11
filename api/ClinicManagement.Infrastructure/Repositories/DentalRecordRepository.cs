using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class DentalRecordRepository : IDentalRecordRepository
{
    private readonly ApplicationDbContext _context;

    public DentalRecordRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DentalRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.DentalRecords
            .Include(dr => dr.Teeth)
            .Include(dr => dr.Acts)
            .FirstOrDefaultAsync(dr => dr.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<DentalRecord>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.DentalRecords
            .Include(dr => dr.Teeth)
            .Include(dr => dr.Acts)
            .Where(dr => dr.PatientId == patientId)
            .OrderByDescending(dr => dr.InterventionDate)
            .ThenByDescending(dr => dr.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DentalRecord>> GetWithUnfinishedActsAsync(
        Guid clinicId,
        DateTime sinceUtc,
        CancellationToken cancellationToken = default)
    {
        // ⚠️ `Any(a => a.IsUnfinished)` stays in the expression tree — it is what keeps a clinical quarter of
        // fiches out of memory. `Acts` is `Include`d whole afterwards, so the caller still sees the séance's
        // other acts; it is the *records* the predicate cuts, not the acts within one.
        return await _context.DentalRecords
            .Include(dr => dr.Acts)
            .Where(dr => dr.ClinicId == clinicId
                         && dr.InterventionDate >= sinceUtc
                         && dr.Acts.Any(a => a.IsUnfinished))
            .OrderByDescending(dr => dr.InterventionDate)
            // Unique last: an OFFSET over a non-unique sort shows one row twice and skips another, which reads
            // as « une séance a disparu ». The caller pages the flattened acts, not this, but the order it
            // flattens has to be stable for the same reason.
            .ThenBy(dr => dr.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Guid AppointmentId, Guid DentalRecordId, decimal Cost)>> GetAppointmentLinksAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> appointmentIds,
        CancellationToken cancellationToken = default)
    {
        if (appointmentIds.Count == 0)
        {
            return Array.Empty<(Guid, Guid, decimal)>();
        }

        // A light projection: no Teeth, no Acts. The caller needs « is there a fiche, and what was it worth »,
        // and loading the graph to answer that is the over-fetch IInvoiceRepository's sibling exists to avoid.
        var rows = await _context.DentalRecords
            .Where(dr => dr.ClinicId == clinicId
                         && dr.AppointmentId != null
                         && appointmentIds.Contains(dr.AppointmentId.Value))
            .Select(dr => new { AppointmentId = dr.AppointmentId!.Value, DentalRecordId = dr.Id, dr.Cost })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.AppointmentId, r.DentalRecordId, r.Cost)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(Guid DentalRecordId, int ToothNumber)>> GetTreatedTeethAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> dentalRecordIds,
        CancellationToken cancellationToken = default)
    {
        if (dentalRecordIds.Count == 0)
        {
            return Array.Empty<(Guid, int)>();
        }

        var ids = dentalRecordIds as ICollection<Guid> ?? dentalRecordIds.ToList();

        // ⚠️ `ClinicId` is compared explicitly: `DentalRecord` is one of the seven clinical tables with no global
        // query filter, so the per-read check is the ONLY tenant guard here — a record id from another practice
        // would otherwise return its teeth.
        var rows = await _context.DentalRecords
            .Where(r => r.ClinicId == clinicId && ids.Contains(r.Id))
            .SelectMany(r => r.Teeth.Select(t => new { DentalRecordId = r.Id, t.ToothNumber }))
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.DentalRecordId, r.ToothNumber)).ToList();
    }

    public async Task<DentalRecord> AddAsync(DentalRecord dentalRecord, CancellationToken cancellationToken = default)
    {
        await _context.DentalRecords.AddAsync(dentalRecord, cancellationToken);
        return dentalRecord;
    }

    public Task UpdateAsync(DentalRecord dentalRecord, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(dentalRecord);
        if (entry.State == EntityState.Detached)
        {
            _context.DentalRecords.Update(dentalRecord);
        }
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await GetByIdAsync(id, cancellationToken);
        if (record != null)
        {
            _context.DentalRecords.Remove(record);
        }
    }
}









