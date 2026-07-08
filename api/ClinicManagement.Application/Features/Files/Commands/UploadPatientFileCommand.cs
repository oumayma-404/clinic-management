using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
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
    private readonly ILogger<UploadPatientFileCommandHandler> _logger;

    public UploadPatientFileCommandHandler(
        IPatientRepository patientRepository,
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ILogger<UploadPatientFileCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<PatientFileDto>> Handle(UploadPatientFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                return Result<PatientFileDto>.Failure("File name is required");
            }

            if (request.FileStream == null)
            {
                return Result<PatientFileDto>.Failure("File stream is required");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null)
            {
                return Result<PatientFileDto>.Failure("Patient not found");
            }

            // Validate folder if provided
            if (request.FolderId.HasValue)
            {
                var folder = await _folderRepository.GetByIdAsync(request.FolderId.Value, cancellationToken);
                if (folder == null || folder.PatientId != request.PatientId)
                {
                    return Result<PatientFileDto>.Failure("Folder not found or does not belong to the patient");
                }
            }

            // Store the blob first, then persist the record. If the DB save fails we must remove
            // the just-stored blob so no orphan remains (FR-C3).
            var storageKey = await _fileStorage.UploadAsync(request.FileStream, request.ContentType, cancellationToken);

            try
            {
                // Determine file type from content type
                var fileType = DetermineFileType(request.ContentType);

                // Create file entity
                var file = new PatientFile(
                    Guid.NewGuid(),
                    request.PatientId,
                    request.FileName,
                    storageKey,
                    request.ContentType,
                    request.FileSize,
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file for patient {PatientId}", request.PatientId);
            return Result<PatientFileDto>.Failure("Error uploading file.");
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









