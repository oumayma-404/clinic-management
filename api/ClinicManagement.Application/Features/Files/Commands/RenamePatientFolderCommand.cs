using ClinicManagement.Application.Common;
using System.Text.Json.Serialization;
using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

/// <summary>
/// Rename a patient folder (AC-4.3) — the first caller of <c>PatientFolder.UpdateName</c>, which had shipped
/// with the entity and never been reachable, so a folder created with a typo could only be deleted and
/// recreated, taking its files with it.
/// </summary>
public class RenamePatientFolderCommand : IRequest<Result<PatientFolderDto>>
{
    [JsonIgnore]
    public Guid PatientId { get; set; }

    [JsonIgnore]
    public Guid FolderId { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class RenamePatientFolderCommandHandler : IRequestHandler<RenamePatientFolderCommand, Result<PatientFolderDto>>
{
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public RenamePatientFolderCommandHandler(
        IPatientFolderRepository folderRepository,
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _folderRepository = folderRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PatientFolderDto>> Handle(RenamePatientFolderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var name = (request.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return Result<PatientFolderDto>.Failure("Le nom du dossier est requis.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientFolderDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientFolderDto>.Failure("Patient introuvable.");
            }

            var folder = await _folderRepository.GetByIdAsync(request.FolderId, cancellationToken);
            if (folder == null || folder.PatientId != request.PatientId)
            {
                return Result<PatientFolderDto>.Failure("Dossier introuvable.");
            }

            // Same uniqueness rule as creation — two « Radiographies » at one level is how the wrong one gets
            // opened for the rest of the patient's file.
            var siblings = await _folderRepository.GetByPatientIdAsync(request.PatientId, cancellationToken);
            if (siblings.Any(f => f.Id != folder.Id
                                  && f.ParentFolderId == folder.ParentFolderId
                                  && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return Result<PatientFolderDto>.Failure("Un dossier portant ce nom existe déjà à cet emplacement.");
            }

            folder.UpdateName(name);

            await _folderRepository.UpdateAsync(folder, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new PatientFolderDto
            {
                Id = folder.Id,
                PatientId = folder.PatientId,
                ParentFolderId = folder.ParentFolderId,
                Name = folder.Name,
                FileCount = folder.Files.Count,
                SubFolderCount = folder.SubFolders.Count,
                CreatedAt = folder.CreatedAt,
                UpdatedAt = folder.UpdatedAt
            };

            return Result<PatientFolderDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientFolderDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
