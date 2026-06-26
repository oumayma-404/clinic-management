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
            .FirstOrDefaultAsync(dr => dr.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<DentalRecord>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.DentalRecords
            .Include(dr => dr.Teeth)
            .Where(dr => dr.PatientId == patientId)
            .OrderByDescending(dr => dr.InterventionDate)
            .ThenByDescending(dr => dr.CreatedAt)
            .ToListAsync(cancellationToken);
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









