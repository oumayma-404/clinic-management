using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Repositories;

public class MedicalDocumentRepository : IMedicalDocumentRepository
{
    private readonly ApplicationDbContext _context;

    public MedicalDocumentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MedicalDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.MedicalDocuments
            .Include(d => d.Patient)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<MedicalDocument>> GetByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        return await _context.MedicalDocuments
            .Include(d => d.Patient)
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.DocumentDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<MedicalDocument>> GetByDocumentTypeAsync(string documentType, CancellationToken cancellationToken = default)
    {
        return await _context.MedicalDocuments
            .Include(d => d.Patient)
            .Where(d => d.DocumentType == documentType)
            .OrderByDescending(d => d.DocumentDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<MedicalDocument>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.MedicalDocuments
            .Include(d => d.Patient)
            .OrderByDescending(d => d.DocumentDate)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(MedicalDocument document, CancellationToken cancellationToken = default)
    {
        await _context.MedicalDocuments.AddAsync(document, cancellationToken);
    }

    public async Task UpdateAsync(MedicalDocument document, CancellationToken cancellationToken = default)
    {
        _context.MedicalDocuments.Update(document);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(MedicalDocument document, CancellationToken cancellationToken = default)
    {
        _context.MedicalDocuments.Remove(document);
        await Task.CompletedTask;
    }
}

