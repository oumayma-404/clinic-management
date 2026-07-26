using MediatR;
using ClinicManagement.Application.Common.Interfaces;
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
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetPatientFilesQueryHandler(
        IPatientFileRepository fileRepository,
        IPatientFolderRepository folderRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver)
    {
        _fileRepository = fileRepository;
        _folderRepository = folderRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<IEnumerable<PatientFileDto>>> Handle(GetPatientFilesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // PatientFile/PatientFolder carry no ClinicId of their own and are excluded from the global query
            // filter, so this explicit check is the sole tenant guard for this read (AC-1/AC-2): resolve the
            // caller's clinic and confirm the owning patient (and the scoped folder, if any) belongs to it.
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<IEnumerable<PatientFileDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<IEnumerable<PatientFileDto>>.Failure("Patient introuvable.");
            }

            IEnumerable<Domain.Entities.PatientFile> files;

            if (request.FolderId.HasValue)
            {
                var folder = await _folderRepository.GetByIdAsync(request.FolderId.Value, cancellationToken);
                if (folder == null || folder.PatientId != request.PatientId)
                {
                    return Result<IEnumerable<PatientFileDto>>.Failure("Dossier introuvable.");
                }

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
