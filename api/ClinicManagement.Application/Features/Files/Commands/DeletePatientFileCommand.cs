using ClinicManagement.Application.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

public class DeletePatientFileCommand : IRequest<Result<bool>>
{
    public Guid PatientId { get; set; }
    public Guid FileId { get; set; }
}

public class DeletePatientFileCommandHandler : IRequestHandler<DeletePatientFileCommand, Result<bool>>
{
    private readonly IPatientFileRepository _fileRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeletePatientFileCommandHandler> _logger;

    public DeletePatientFileCommandHandler(
        IPatientFileRepository fileRepository,
        IPatientRepository patientRepository,
        IFileStorage fileStorage,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeletePatientFileCommandHandler> logger)
    {
        _fileRepository = fileRepository;
        _patientRepository = patientRepository;
        _fileStorage = fileStorage;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(DeletePatientFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<bool>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var file = await _fileRepository.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null)
            {
                return Result<bool>.Failure("Fichier introuvable.");
            }

            if (file.PatientId != request.PatientId)
            {
                return Result<bool>.Failure("Ce fichier n'appartient pas à ce patient.");
            }

            // Verify the owning patient belongs to the caller's clinic before deleting (AC-1).
            var patient = await _patientRepository.GetByIdAsync(file.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<bool>.Failure("Fichier introuvable.");
            }

            // Remove the DB record first and commit; the blob is deleted only AFTER the commit so a failed
            // save never strands the record on a missing blob. A blob-delete failure is logged (a leaked blob
            // is preferable to an orphaned record) — same ordering as DeletePatientFolderCommand (#18/AC-3).
            // ⚠️ A coffre file has no *original* blob here and its original is NOT erased: those bytes sit on the
            // practice's own disk, under a ten-to-twenty-year retention duty, and the app does not destroy what it
            // does not host. The row goes; an orphan on the cabinet's disk is recoverable, a deletion is not.
            // ⚠️ **A coffre file still owns one hosted blob — its preview.** That one IS ours and must go with the
            // row, or every deleted study leaves an object nothing points at, for the life of the deployment.
            var storageKeys = PatientFileBlobs.OwnedBy(file).ToList();
            await _fileRepository.DeleteAsync(file, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            foreach (var storageKey in storageKeys)
            {
                try
                {
                    await _fileStorage.DeleteAsync(storageKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete blob {StorageKey} after removing file {FileId}; the record is already deleted.", storageKey, file.Id);
                }
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<bool>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
