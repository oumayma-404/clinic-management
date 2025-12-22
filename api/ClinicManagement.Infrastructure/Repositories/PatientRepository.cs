using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class PatientRepository : IPatientRepository
{
    private readonly ApplicationDbContext _context;

    public PatientRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Patient?> GetByIdWithAppointmentsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .Include(p => p.Appointments)
            .Include(p => p.Flags)
            .Include(p => p.Files)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Patient>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Patient>> GetFlaggedPatientsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .Include(p => p.Flags.Where(f => f.IsActive))
            .Where(p => p.Flags.Any(f => f.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<Patient> AddAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        await _context.Patients.AddAsync(patient, cancellationToken);
        return patient;
    }

    public Task UpdateAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        _context.Patients.Update(patient);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var patient = await GetByIdAsync(id, cancellationToken);
        if (patient != null)
        {
            _context.Patients.Remove(patient);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Patients.AnyAsync(p => p.Id == id, cancellationToken);
    }
}



