using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

public class DeletePatientFolderCommand : IRequest<Result<bool>>
{
    public Guid PatientId { get; set; }
    public Guid FolderId { get; set; }
}

public class DeletePatientFolderCommandHandler : IRequestHandler<DeletePatientFolderCommand, Result<bool>>
{
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientFileRepository _fileRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeletePatientFolderCommandHandler> _logger;

    public DeletePatientFolderCommandHandler(
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IPatientRepository patientRepository,
        IFileStorage fileStorage,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeletePatientFolderCommandHandler> logger)
    {
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _patientRepository = patientRepository;
        _fileStorage = fileStorage;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(DeletePatientFolderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<bool>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var folder = await _folderRepository.GetByIdAsync(request.FolderId, cancellationToken);
            if (folder == null)
            {
                return Result<bool>.Failure("Dossier introuvable.");
            }

            if (folder.PatientId != request.PatientId)
            {
                return Result<bool>.Failure("Ce dossier n'appartient pas à ce patient.");
            }

            // Verify the owning patient belongs to the caller's clinic before deleting (AC-1).
            var patient = await _patientRepository.GetByIdAsync(folder.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<bool>.Failure("Dossier introuvable.");
            }

            // Note: Nested folders are not supported in the UI, so we don't check for subfolders.

            // Stage the DB deletions (files + folder) and commit them together FIRST, so we never leave a file
            // row pointing at a deleted folder (or the folder removed while its file rows survive). The blobs
            // are deleted only AFTER the DB commit: a mid-loop storage error can no longer skip a DB delete,
            // and a leaked blob is preferable to an orphaned record (AC-3, bug #18).
            // ⚠️ Coffre files contribute no key: their originals live on the practice's own disk and are left
            // there, exactly as DeletePatientFileCommand leaves them.
            var filesInFolder = await _fileRepository.GetByFolderIdAsync(request.FolderId, cancellationToken);
            var storageKeys = filesInFolder
                .Where(f => f.Residency == FileResidency.Hosted && f.StorageKey != null)
                .Select(f => f.StorageKey!)
                .ToList();

            foreach (var file in filesInFolder)
            {
                await _fileRepository.DeleteAsync(file, cancellationToken);
            }

            await _folderRepository.DeleteAsync(folder, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Best-effort blob cleanup after the commit; log (never swallow silently) so a leaked blob is diagnosable.
            foreach (var storageKey in storageKeys)
            {
                try
                {
                    await _fileStorage.DeleteAsync(storageKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to delete blob {StorageKey} after removing folder {FolderId}; the file record is already deleted.",
                        storageKey, request.FolderId);
                }
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error deleting folder {FolderId}", request.FolderId);
            return Result<bool>.Failure($"Error deleting folder: {ex.Message}");
        }
    }
}
