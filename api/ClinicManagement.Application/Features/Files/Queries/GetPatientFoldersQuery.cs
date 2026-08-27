using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
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
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientFoldersQueryHandler(
        IPatientFolderRepository folderRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _folderRepository = folderRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<PatientFolderDto>>> Handle(GetPatientFoldersQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // PatientFolder carries no ClinicId of its own and is excluded from the global query filter, so
            // this explicit check is the sole tenant guard for this read (AC-1/AC-2): resolve the caller's
            // clinic and confirm the owning patient (and the scoped parent folder, if any) belongs to it.
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IEnumerable<PatientFolderDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<IEnumerable<PatientFolderDto>>.Failure("Patient introuvable.");
            }

            IEnumerable<Domain.Entities.PatientFolder> folders;

            if (request.ParentFolderId.HasValue)
            {
                var parentFolder = await _folderRepository.GetByIdAsync(request.ParentFolderId.Value, cancellationToken);
                if (parentFolder == null || parentFolder.PatientId != request.PatientId)
                {
                    return Result<IEnumerable<PatientFolderDto>>.Failure("Dossier introuvable.");
                }

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
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<PatientFolderDto>>.Failure($"Error retrieving folders: {ex.Message}");
        }
    }
}
