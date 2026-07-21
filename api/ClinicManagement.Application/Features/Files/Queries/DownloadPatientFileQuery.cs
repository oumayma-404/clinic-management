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
    private readonly IPatientRepository _patientRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ICurrentClinicResolver _clinicResolver;

    public DownloadPatientFileQueryHandler(
        IPatientFileRepository fileRepository,
        IPatientRepository patientRepository,
        IFileStorage fileStorage,
        ICurrentClinicResolver clinicResolver)
    {
        _fileRepository = fileRepository;
        _patientRepository = patientRepository;
        _fileStorage = fileStorage;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<FileDownloadDto>> Handle(DownloadPatientFileQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<FileDownloadDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var file = await _fileRepository.GetByIdAsync(request.FileId, cancellationToken);
            if (file == null)
            {
                return Result<FileDownloadDto>.Failure("File not found");
            }

            if (file.PatientId != request.PatientId)
            {
                return Result<FileDownloadDto>.Failure("File does not belong to the specified patient");
            }

            // Verify the owning patient belongs to the caller's clinic before streaming any bytes (AC-1).
            var patient = await _patientRepository.GetByIdAsync(file.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<FileDownloadDto>.Failure("File not found");
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
