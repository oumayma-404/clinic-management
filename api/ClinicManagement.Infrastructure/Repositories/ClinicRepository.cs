using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class ClinicRepository : IClinicRepository
{
    private readonly ApplicationDbContext _context;

    public ClinicRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Clinic?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Clinics
            .Include(c => c.Users)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Clinic?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.Clinics
            .Include(c => c.Users)
            .FirstOrDefaultAsync(c => c.Code == code, cancellationToken);
    }

    public async Task<Clinic?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.Clinics
            .Include(c => c.Users)
            .FirstOrDefaultAsync(c => c.Name == name, cancellationToken);
    }

    public async Task<IEnumerable<Clinic>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Clinics
            .Include(c => c.Users)
            .ToListAsync(cancellationToken);
    }

    public async Task<Clinic> AddAsync(Clinic clinic, CancellationToken cancellationToken = default)
    {
        await _context.Clinics.AddAsync(clinic, cancellationToken);
        return clinic;
    }

    public async Task UpdateAsync(Clinic clinic, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(clinic);
        if (entry.State == EntityState.Detached)
        {
            _context.Clinics.Update(clinic);
        }
        await Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Clinics.AnyAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
            
        return await _context.Clinics.AnyAsync(c => c.Code == code, cancellationToken);
    }
}




