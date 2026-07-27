using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

public class CreatePatientFolderCommand : IRequest<Result<PatientFolderDto>>
{
    public Guid PatientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
}

public class CreatePatientFolderCommandHandler : IRequestHandler<CreatePatientFolderCommand, Result<PatientFolderDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public CreatePatientFolderCommandHandler(
        IPatientRepository patientRepository,
        IPatientFolderRepository folderRepository,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _patientRepository = patientRepository;
        _folderRepository = folderRepository;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<PatientFolderDto>> Handle(CreatePatientFolderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Result<PatientFolderDto>.Failure("Le nom du dossier est requis.");
            }

            // Authoritative tenant guard: resolve the caller's clinic from the DB and verify the patient
            // belongs to it before creating a folder (defense-in-depth, independent of the fail-open global
            // filter — cloud-security-and-tenant-isolation #6).
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

            // Validate parent folder if provided
            if (request.ParentFolderId.HasValue)
            {
                var parentFolder = await _folderRepository.GetByIdAsync(request.ParentFolderId.Value, cancellationToken);
                if (parentFolder == null || parentFolder.PatientId != request.PatientId)
                {
                    return Result<PatientFolderDto>.Failure("Dossier parent introuvable ou n'appartenant pas à ce patient.");
                }
            }

            // Check if folder with same name already exists in the same location
            var existingFolders = await _folderRepository.GetByPatientIdAsync(request.PatientId, cancellationToken);
            var folderExists = existingFolders.Any(f => 
                f.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase) && 
                f.ParentFolderId == request.ParentFolderId);
            
            if (folderExists)
            {
                return Result<PatientFolderDto>.Failure("Un dossier portant ce nom existe déjà à cet emplacement.");
            }

            var folder = new PatientFolder(
                Guid.NewGuid(),
                request.PatientId,
                request.Name.Trim(),
                request.ParentFolderId);

            await _folderRepository.AddAsync(folder, cancellationToken);
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
            return Result<PatientFolderDto>.Failure($"Error creating folder: {ex.Message}");
        }
    }
}









