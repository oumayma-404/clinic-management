using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class PatientFileRepository : IPatientFileRepository
{
    private readonly ApplicationDbContext _context;

    public PatientFileRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PatientFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFiles
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<PatientFile>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFiles
            .Include(f => f.Folder)
            .Where(f => f.PatientId == patientId)
            .OrderByDescending(f => f.UploadedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<PatientFile>> GetByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFiles
            .Include(f => f.Folder)
            .Where(f => f.FolderId == folderId)
            .OrderByDescending(f => f.UploadedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<PatientFile>> GetRootFilesByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFiles
            .Include(f => f.Folder)
            .Where(f => f.PatientId == patientId && f.FolderId == null)
            .OrderByDescending(f => f.UploadedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(PatientFile file, CancellationToken cancellationToken = default)
    {
        await _context.PatientFiles.AddAsync(file, cancellationToken);
    }

    public async Task UpdateAsync(PatientFile file, CancellationToken cancellationToken = default)
    {
        _context.PatientFiles.Update(file);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(PatientFile file, CancellationToken cancellationToken = default)
    {
        _context.PatientFiles.Remove(file);
        await Task.CompletedTask;
    }
}









