using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Queries;

public class GetPatientFoldersQuery : IRequest<Result<IEnumerable<PatientFolderDto>>>
{
    public Guid PatientId { get; set; }
    public Guid? ParentFolderId { get; set; } // Null means root folders
}

public class GetPatientFoldersQueryHandler : IRequestHandler<GetPatientFoldersQuery, Result<IEnumerable<PatientFolderDto>>>
{
    private readonly IPatientFolderRepository _folderRepository;

    public GetPatientFoldersQueryHandler(IPatientFolderRepository folderRepository)
    {
        _folderRepository = folderRepository;
    }

    public async Task<Result<IEnumerable<PatientFolderDto>>> Handle(GetPatientFoldersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            IEnumerable<Domain.Entities.PatientFolder> folders;

            if (request.ParentFolderId.HasValue)
            {
                folders = await _folderRepository.GetSubFoldersAsync(request.ParentFolderId.Value, cancellationToken);
            }
            else
            {
                folders = await _folderRepository.GetRootFoldersByPatientIdAsync(request.PatientId, cancellationToken);
            }

            var dtos = folders.Select(f => new PatientFolderDto
            {
                Id = f.Id,
                PatientId = f.PatientId,
                ParentFolderId = f.ParentFolderId,
                Name = f.Name,
                FileCount = f.Files.Count,
                SubFolderCount = f.SubFolders.Count,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt
            });

            return Result<IEnumerable<PatientFolderDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<PatientFolderDto>>.Failure($"Error retrieving folders: {ex.Message}");
        }
    }
}









