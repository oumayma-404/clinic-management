using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class PatientFileAnnotationRepository : IPatientFileAnnotationRepository
{
    private readonly ApplicationDbContext _context;

    public PatientFileAnnotationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PatientFileAnnotation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.PatientFileAnnotations.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PatientFileAnnotation>> GetForFileAsync(
        Guid fileId,
        CancellationToken cancellationToken = default)
        => await _context.PatientFileAnnotations
            .Where(a => a.PatientFileId == fileId)
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(PatientFileAnnotation annotation, CancellationToken cancellationToken = default)
        => await _context.PatientFileAnnotations.AddAsync(annotation, cancellationToken);

    public Task UpdateAsync(PatientFileAnnotation annotation, CancellationToken cancellationToken = default)
    {
        _context.PatientFileAnnotations.Update(annotation);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(PatientFileAnnotation annotation, CancellationToken cancellationToken = default)
    {
        _context.PatientFileAnnotations.Remove(annotation);
        return Task.CompletedTask;
    }
}
