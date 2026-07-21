using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
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
                return Result<bool>.Failure("File not found");
            }

            if (file.PatientId != request.PatientId)
            {
                return Result<bool>.Failure("File does not belong to the specified patient");
            }

            // Verify the owning patient belongs to the caller's clinic before deleting (AC-1).
            var patient = await _patientRepository.GetByIdAsync(file.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<bool>.Failure("File not found");
            }

            // Remove the DB record first and commit; the blob is deleted only AFTER the commit so a failed
            // save never strands the record on a missing blob. A blob-delete failure is logged (a leaked blob
            // is preferable to an orphaned record) — same ordering as DeletePatientFolderCommand (#18/AC-3).
            var storageKey = file.StorageKey;
            await _fileRepository.DeleteAsync(file, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            try
            {
                await _fileStorage.DeleteAsync(storageKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete blob {StorageKey} after removing file {FileId}; the record is already deleted.", storageKey, file.Id);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Error deleting file: {ex.Message}");
        }
    }
}
