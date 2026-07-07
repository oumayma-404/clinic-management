using ClinicManagement.Application.Common.Interfaces;
using MediatR;
using ClinicManagement.Application.Common.Models;
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
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;

    public DeletePatientFolderCommandHandler(
        IPatientFolderRepository folderRepository,
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork)
    {
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(DeletePatientFolderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var folder = await _folderRepository.GetByIdAsync(request.FolderId, cancellationToken);
            if (folder == null)
            {
                return Result<bool>.Failure("Folder not found");
            }

            if (folder.PatientId != request.PatientId)
            {
                return Result<bool>.Failure("Folder does not belong to the specified patient");
            }

            // Note: Nested folders are not supported in the UI, so we don't check for subfolders
            // All folders are root-level folders, so we can safely delete any folder

            // Delete all files in the folder
            var filesInFolder = await _fileRepository.GetByFolderIdAsync(request.FolderId, cancellationToken);
            foreach (var file in filesInFolder)
            {
                try
                {
                    // Delete from storage
                    await _fileStorage.DeleteAsync(file.StorageKey, cancellationToken);
                    // Delete from database
                    await _fileRepository.DeleteAsync(file, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log but continue deleting other files
                    // In production, you might want to log this properly
                    System.Diagnostics.Debug.WriteLine($"Error deleting file {file.Id}: {ex.Message}");
                }
            }

            // Delete the folder
            await _folderRepository.DeleteAsync(folder, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure($"Error deleting folder: {ex.Message}");
        }
    }
}


