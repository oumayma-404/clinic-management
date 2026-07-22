using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IToothStateRepository"/> (the persistent odontogram). Child-of-patient
/// with no <c>ClinicId</c>; tenant isolation is enforced at the handler by loading the owning patient.
/// Mutations only stage changes; the UnitOfWork commits.
/// </summary>
public class ToothStateRepository : IToothStateRepository
{
    private readonly ApplicationDbContext _context;

    public ToothStateRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ToothState>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.ToothStates
            .Where(t => t.PatientId == patientId)
            .OrderBy(t => t.ToothNumber)
            .ThenBy(t => t.TreatmentDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ToothState>> GetByDentalRecordIdAsync(Guid dentalRecordId, CancellationToken cancellationToken = default)
    {
        return await _context.ToothStates
            .Where(t => t.DentalRecordId == dentalRecordId)
            .ToListAsync(cancellationToken);
    }

    public async Task<ToothState> AddAsync(ToothState toothState, CancellationToken cancellationToken = default)
    {
        await _context.ToothStates.AddAsync(toothState, cancellationToken);
        return toothState;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var toothState = await _context.ToothStates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (toothState != null)
        {
            _context.ToothStates.Remove(toothState);
        }
    }
}
