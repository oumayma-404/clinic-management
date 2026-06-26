using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class DoctorRepository : IDoctorRepository
{
    private readonly ApplicationDbContext _context;

    public DoctorRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Doctor?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Doctors
            .Include(d => d.Clinic)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Doctor>> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return await _context.Doctors
            .Where(d => d.ClinicId == clinicId)
            .OrderBy(d => d.LastName)
            .ThenBy(d => d.FirstName)
            .ToListAsync(cancellationToken);
    }

    public async Task<Doctor?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.Doctors
            .Include(d => d.Clinic)
            .FirstOrDefaultAsync(d => d.UserId == userId, cancellationToken);
    }

    public async Task AddAsync(Doctor entity, CancellationToken cancellationToken = default)
    {
        await _context.Doctors.AddAsync(entity, cancellationToken);
    }

    public void Update(Doctor entity)
    {
        _context.Doctors.Update(entity);
    }

    public void Remove(Doctor entity)
    {
        _context.Doctors.Remove(entity);
    }
}


