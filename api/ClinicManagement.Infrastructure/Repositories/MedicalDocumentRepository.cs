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

    /// <summary>
    /// The one deliberately scope-independent read here: <c>IgnoreQueryFilters()</c> because the caller
    /// (<c>PdfGenerationJob</c>) is asking this question precisely in order to *set* the scope. A projection of a
    /// single Guid, so nothing of another tenant's document is materialised.
    /// </summary>
    public async Task<Guid?> GetOwningClinicIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.MedicalDocuments
            .IgnoreQueryFilters()
            .Where(d => d.Id == id)
            .Select(d => (Guid?)d.Patient.ClinicId)
            .FirstOrDefaultAsync(cancellationToken);
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

    public async Task<IEnumerable<MedicalDocument>> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Scope to the clinic in SQL via the owning patient (MedicalDocument has no ClinicId of its own),
        // so other tenants' documents are never materialized into memory.
        return await _context.MedicalDocuments
            .Include(d => d.Patient)
            .Where(d => d.Patient != null && d.Patient.ClinicId == clinicId)
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
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(document);
        if (entry.State == EntityState.Detached)
        {
            _context.MedicalDocuments.Update(document);
        }
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(MedicalDocument document, CancellationToken cancellationToken = default)
    {
        _context.MedicalDocuments.Remove(document);
        await Task.CompletedTask;
    }
}

