using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Files.Commands;

public class UploadPatientFileCommand : IRequest<Result<PatientFileDto>>
{
    public Guid PatientId { get; set; }
    public Guid? FolderId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public Stream FileStream { get; set; } = null!;
    public string? Description { get; set; }
    public string? UploadedBy { get; set; }
}

public class UploadPatientFileCommandHandler : IRequestHandler<UploadPatientFileCommand, Result<PatientFileDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientFileRepository _fileRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<UploadPatientFileCommandHandler> _logger;

    public UploadPatientFileCommandHandler(
        IPatientRepository patientRepository,
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        ILogger<UploadPatientFileCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<PatientFileDto>> Handle(UploadPatientFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                return Result<PatientFileDto>.Failure("Le nom du fichier est requis.");
            }

            if (request.FileStream == null)
            {
                return Result<PatientFileDto>.Failure("Le contenu du fichier est requis.");
            }

            // Authoritative tenant guard: resolve the caller's clinic from the DB and verify the patient
            // belongs to it before storing any file (defense-in-depth, independent of the fail-open global
            // filter — cloud-security-and-tenant-isolation #6).
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientFileDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientFileDto>.Failure("Patient introuvable.");
            }

            // Validate folder if provided
            if (request.FolderId.HasValue)
            {
                var folder = await _folderRepository.GetByIdAsync(request.FolderId.Value, cancellationToken);
                if (folder == null || folder.PatientId != request.PatientId)
                {
                    return Result<PatientFileDto>.Failure("Dossier introuvable ou n'appartenant pas à ce patient.");
                }
            }

            // US-11 / AC-11.1–11.5: validate BEFORE anything is written, so a refused upload leaves no blob
            // and no row — no orphan cleanup required. Previously ANY client-declared content type was
            // accepted, with no allow-list, no signature check and no size cap, and the declared type was
            // then echoed back on download from the app's own origin (audit § 2, finding 12).
            var contentType = FileContentValidation.Normalize(
                request.ContentType, FileContentValidation.PatientFileTypes);
            if (contentType is null)
            {
                return Result<PatientFileDto>.Failure(FileContentValidation.UnsupportedPatientFileMessage);
            }

            // Buffer under a hard cap so an oversized upload cannot be used to exhaust memory, and so the
            // leading bytes can be inspected — a Content-Type header is trivially spoofable.
            using var buffer = new MemoryStream();
            await request.FileStream.CopyToAsync(buffer, cancellationToken);

            if (buffer.Length == 0)
            {
                return Result<PatientFileDto>.Failure(FileContentValidation.EmptyFileMessage);
            }

            if (buffer.Length > FileContentValidation.MaxPatientFileBytes)
            {
                return Result<PatientFileDto>.Failure(
                    FileContentValidation.TooLargeMessage(FileContentValidation.MaxPatientFileBytes));
            }

            if (!FileContentValidation.MatchesSignature(contentType, buffer.ToArray()))
            {
                return Result<PatientFileDto>.Failure(FileContentValidation.SignatureMismatchMessage);
            }

            // Store the blob first, then persist the record. If the DB save fails we must remove
            // the just-stored blob so no orphan remains (FR-C3).
            buffer.Position = 0;
            var storageKey = await _fileStorage.UploadAsync(
                buffer, contentType, patient.ClinicId, cancellationToken);

            try
            {
                // Persist the VALIDATED type and the ACTUAL byte count, never the client's claims — the stored
                // type is what the download endpoint serves back (AC-11.6), and a client-supplied FileSize
                // could disagree with what was written.
                var fileType = DetermineFileType(contentType);

                // Create file entity
                var file = new PatientFile(
                    Guid.NewGuid(),
                    request.PatientId,
                    request.FileName,
                    storageKey,
                    contentType,
                    buffer.Length,
                    fileType,
                    request.FolderId,
                    request.Description,
                    request.UploadedBy);

                await _fileRepository.AddAsync(file, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var dto = new PatientFileDto
                {
                    Id = file.Id,
                    PatientId = file.PatientId,
                    FolderId = file.FolderId,
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    FileSize = file.FileSize,
                    FileType = file.FileType.ToString(),
                    Description = file.Description,
                    UploadedAt = file.UploadedAt,
                    UploadedBy = file.UploadedBy
                };

                return Result<PatientFileDto>.Success(dto);
            }
            catch
            {
                try { await _fileStorage.DeleteAsync(storageKey, cancellationToken); }
                catch { /* best-effort orphan cleanup: don't mask the original failure */ }
                throw;
            }
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error uploading file for patient {PatientId}", request.PatientId);
            return Result<PatientFileDto>.Failure("Erreur lors de l'envoi du fichier.");
        }
    }

    private static FileType DetermineFileType(string contentType)
    {
        if (contentType.Contains("image") || contentType.Contains("dicom"))
            return FileType.Scan;
        
        if (contentType.Contains("pdf") || contentType.Contains("document"))
            return FileType.MedicalRecord;
        
        if (contentType.Contains("text") || contentType.Contains("csv"))
            return FileType.LabResult;
        
        return FileType.Other;
    }
}









