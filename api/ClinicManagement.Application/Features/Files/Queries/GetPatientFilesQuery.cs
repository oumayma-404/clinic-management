using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Queries;

/// <summary>
/// One page of a patient's files (AC-5.9). It used to return every file the patient had, unbounded — a drawer
/// of CBCT studies and years of radiographs, cut in the browser.
/// </summary>
public class GetPatientFilesQuery : IRequest<Result<PagedResult<PatientFileDto>>>
{
    public Guid PatientId { get; set; }
    public Guid? FolderId { get; set; } // Null means root files
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public class GetPatientFilesQueryHandler : IRequestHandler<GetPatientFilesQuery, Result<PagedResult<PatientFileDto>>>
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

    public async Task<Result<PagedResult<PatientFileDto>>> Handle(GetPatientFilesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // PatientFile/PatientFolder carry no ClinicId of their own and are excluded from the global query
            // filter, so this explicit check is the sole tenant guard for this read (AC-1/AC-2): resolve the
            // caller's clinic and confirm the owning patient (and the scoped folder, if any) belongs to it.
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PagedResult<PatientFileDto>>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PagedResult<PatientFileDto>>.Failure("Patient introuvable.");
            }

            if (request.FolderId.HasValue)
            {
                var folder = await _folderRepository.GetByIdAsync(request.FolderId.Value, cancellationToken);
                if (folder == null || folder.PatientId != request.PatientId)
                {
                    return Result<PagedResult<PatientFileDto>>.Failure("Dossier introuvable.");
                }
            }

            var files = await _fileRepository.GetPageAsync(
                request.PatientId,
                request.FolderId,
                PageRequest.From(request.Page, request.PageSize),
                cancellationToken);

            var dtos = files.Map(f => f.ToDto());

            return Result<PagedResult<PatientFileDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PagedResult<PatientFileDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
