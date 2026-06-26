using MediatR;
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
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public DeletePatientFileCommandHandler(
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork)
    {
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeletePatientFileCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _fileRepository.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null)
            {
                return Result<bool>.Failure("File not found");
            }

            if (file.PatientId != request.PatientId)
            {
                return Result<bool>.Failure("File does not belong to the specified patient");
            }

            // Delete from MinIO
            await _fileStorage.DeleteAsync(file.StorageKey, cancellationToken);

            // Delete from database
            await _fileRepository.DeleteAsync(file, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Error deleting file: {ex.Message}");
        }
    }
}









