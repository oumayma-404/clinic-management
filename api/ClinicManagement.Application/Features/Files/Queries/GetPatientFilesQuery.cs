using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Queries;

public class GetPatientFilesQuery : IRequest<Result<IEnumerable<PatientFileDto>>>
{
    public Guid PatientId { get; set; }
    public Guid? FolderId { get; set; } // Null means root files
}

public class GetPatientFilesQueryHandler : IRequestHandler<GetPatientFilesQuery, Result<IEnumerable<PatientFileDto>>>
{
    private readonly IPatientFileRepository _fileRepository;

    public GetPatientFilesQueryHandler(IPatientFileRepository fileRepository)
    {
        _fileRepository = fileRepository;
    }

    public async Task<Result<IEnumerable<PatientFileDto>>> Handle(GetPatientFilesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            IEnumerable<Domain.Entities.PatientFile> files;

            if (request.FolderId.HasValue)
            {
                files = await _fileRepository.GetByFolderIdAsync(request.FolderId.Value, cancellationToken);
            }
            else
            {
                files = await _fileRepository.GetRootFilesByPatientIdAsync(request.PatientId, cancellationToken);
            }

            var dtos = files.Select(f => new PatientFileDto
            {
                Id = f.Id,
                PatientId = f.PatientId,
                FolderId = f.FolderId,
                FileName = f.FileName,
                ContentType = f.ContentType,
                FileSize = f.FileSize,
                FileType = f.FileType.ToString(),
                Description = f.Description,
                UploadedAt = f.UploadedAt,
                UploadedBy = f.UploadedBy
            });

            return Result<IEnumerable<PatientFileDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<PatientFileDto>>.Failure($"Error retrieving files: {ex.Message}");
        }
    }
}









