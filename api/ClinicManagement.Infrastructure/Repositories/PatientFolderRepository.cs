using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class PatientFolderRepository : IPatientFolderRepository
{
    private readonly ApplicationDbContext _context;

    public PatientFolderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PatientFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFolders
            .Include(f => f.Files)
            .Include(f => f.SubFolders)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<PatientFolder>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFolders
            .Include(f => f.Files)
            .Include(f => f.SubFolders)
            .Where(f => f.PatientId == patientId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<PatientFolder>> GetRootFoldersByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFolders
            .Include(f => f.Files)
            .Include(f => f.SubFolders)
            .Where(f => f.PatientId == patientId && f.ParentFolderId == null)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<PatientFolder>> GetSubFoldersAsync(Guid parentFolderId, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFolders
            .Include(f => f.Files)
            .Include(f => f.SubFolders)
            .Where(f => f.ParentFolderId == parentFolderId)
            .ToListAsync(cancellationToken);
    }

    public async Task<PatientFolder?> GetByNameAndPatientIdAsync(string name, Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFolders
            .FirstOrDefaultAsync(f => f.PatientId == patientId && f.Name.ToLower() == name.ToLower() && f.ParentFolderId == null, cancellationToken);
    }

    public async Task AddAsync(PatientFolder folder, CancellationToken cancellationToken = default)
    {
        await _context.PatientFolders.AddAsync(folder, cancellationToken);
    }

    public async Task UpdateAsync(PatientFolder folder, CancellationToken cancellationToken = default)
    {
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(folder);
        if (entry.State == EntityState.Detached)
        {
            _context.PatientFolders.Update(folder);
        }
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(PatientFolder folder, CancellationToken cancellationToken = default)
    {
        _context.PatientFolders.Remove(folder);
        await Task.CompletedTask;
    }
}


