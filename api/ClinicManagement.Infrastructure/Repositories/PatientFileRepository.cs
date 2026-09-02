using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
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

    public async Task<VaultContentTotals> GetVaultTotalsAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // One aggregate, both figures. `IgnoreQueryFilters` because one caller is the daily pass, which runs
        // UseSystemWide with no clinic in scope; the clinicId parameter is the authoritative check, as it is in the
        // staleness reads beside it.
        var totals = await _context.PatientFiles
            .IgnoreQueryFilters()
            .Where(f => f.ClinicId == clinicId && f.Residency == FileResidency.Vault)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Bytes = g.Sum(f => f.FileSize) })
            .FirstOrDefaultAsync(cancellationToken);

        // No rows at all means no group, not a zeroed one — an empty coffre is a legitimate answer, not a failure.
        return totals is null ? VaultContentTotals.Empty : new VaultContentTotals(totals.Count, totals.Bytes);
    }

    public async Task<PatientFile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.PatientFiles
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<PagedResult<PatientFile>> GetPageAsync(
        Guid patientId,
        Guid? folderId,
        PageRequest? paging,
        CancellationToken cancellationToken = default)
    {
        var query = _context.PatientFiles
            .Include(f => f.Folder)
            .Where(f => f.PatientId == patientId && f.FolderId == folderId);

        var total = await query.CountAsync(cancellationToken);

        // `.ThenBy(Id)` is not tidiness: two files uploaded in the same tick sort arbitrarily under OFFSET, so
        // one can appear on two pages while another is skipped — which reads as « a scan disappeared ».
        var ordered = query
            .OrderByDescending(f => f.UploadedAt)
            .ThenBy(f => f.Id);

        if (paging is not { } page)
        {
            return PagedResult<PatientFile>.Unpaged(await ordered.ToListAsync(cancellationToken));
        }

        var items = await ordered.Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);
        return new PagedResult<PatientFile>(items, page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<ClinicFileManifestRow>> GetClinicManifestPageAsync(
        Guid clinicId,
        PageRequest? paging,
        CancellationToken cancellationToken = default)
    {
        // The join is the point: one query returns the patient's name beside each file, where the caller would
        // otherwise read the patient table once per file to build a folder tree.
        var query =
            from file in _context.PatientFiles
            join patient in _context.Patients on file.PatientId equals patient.Id
            where file.ClinicId == clinicId
            // Ascending, and see the interface note: a caller walks these pages while uploads continue, and
            // newest-first would push unread rows past the cursor every time somebody scans a document.
            orderby file.UploadedAt, file.Id
            select new ClinicFileManifestRow(
                file.Id,
                file.PatientId,
                patient.FirstName + " " + patient.LastName,
                file.FileName,
                file.ContentType,
                file.FileSize,
                file.UploadedAt);

        var total = await query.CountAsync(cancellationToken);

        if (paging is not { } page)
        {
            return PagedResult<ClinicFileManifestRow>.Unpaged(await query.ToListAsync(cancellationToken));
        }

        var items = await query.Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);
        return new PagedResult<ClinicFileManifestRow>(items, page.Page, page.PageSize, total);
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
        // Only attach when the caller handed us a DETACHED instance. On the normal path the handler loaded
        // the aggregate through this same DbContext, so it is already tracked and change tracking has the
        // real original values — including the xmin concurrency token. Calling Update() on a tracked entity
        // instead re-marks every property modified, and on a detached one that was never loaded the token
        // reads as 0, producing "WHERE xmin = 0", zero matched rows and a 409 for a conflict that never was.
        var entry = _context.Entry(file);
        if (entry.State == EntityState.Detached)
        {
            _context.PatientFiles.Update(file);
        }
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(PatientFile file, CancellationToken cancellationToken = default)
    {
        _context.PatientFiles.Remove(file);
        await Task.CompletedTask;
    }
}









