using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Queries;

public class DownloadPatientFileQuery : IRequest<Result<FileDownloadDto>>
{
    public Guid PatientId { get; set; }
    public Guid FileId { get; set; }
}

public class FileDownloadDto
{
    public Stream FileStream { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
}

public class DownloadPatientFileQueryHandler : IRequestHandler<DownloadPatientFileQuery, Result<FileDownloadDto>>
{
    private readonly IPatientFileRepository _fileRepository;
    private readonly IFileStorage _fileStorage;

    public DownloadPatientFileQueryHandler(
        IPatientFileRepository fileRepository,
        IFileStorage fileStorage)
    {
        _fileRepository = fileRepository;
        _fileStorage = fileStorage;
    }

    public async Task<Result<FileDownloadDto>> Handle(DownloadPatientFileQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await _fileRepository.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null)
            {
                return Result<FileDownloadDto>.Failure("File not found");
            }

            if (file.PatientId != request.PatientId)
            {
                return Result<FileDownloadDto>.Failure("File does not belong to the specified patient");
            }

            var fileStream = await _fileStorage.DownloadAsync(file.StorageKey, cancellationToken);

            var dto = new FileDownloadDto
            {
                FileStream = fileStream,
                FileName = file.FileName,
                ContentType = file.ContentType
            };

            return Result<FileDownloadDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<FileDownloadDto>.Failure($"Error downloading file: {ex.Message}");
        }
    }
}









