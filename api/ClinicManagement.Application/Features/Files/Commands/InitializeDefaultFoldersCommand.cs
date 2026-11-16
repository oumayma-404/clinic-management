using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files.Commands;

public class InitializeDefaultFoldersCommand : IRequest<Result<IEnumerable<PatientFolderDto>>>
{
    public Guid PatientId { get; set; }
}

public class InitializeDefaultFoldersCommandHandler : IRequestHandler<InitializeDefaultFoldersCommand, Result<IEnumerable<PatientFolderDto>>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientFolderRepository _folderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public InitializeDefaultFoldersCommandHandler(
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

    public async Task<Result<IEnumerable<PatientFolderDto>>> Handle(InitializeDefaultFoldersCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Authoritative tenant guard: resolve the caller's clinic from the DB and verify the patient
            // belongs to it before creating folders (defense-in-depth, independent of the fail-open global
            // filter — cloud-security-and-tenant-isolation #6).
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

            // « Missing » is asked by id first, then by name: a renamed default folder still holds its id, and creating
            // it again under that id is refused before any SQL runs (features/camera-photo-import).
            await DefaultPatientFolders.EnsureAllAsync(_folderRepository, _unitOfWork, patient, cancellationToken);

            // Return all root folders (including newly created ones)
            var allFolders = await _folderRepository.GetRootFoldersByPatientIdAsync(request.PatientId, cancellationToken);
            var dtos = allFolders.Select(DefaultPatientFolders.ToDto);

            return Result<IEnumerable<PatientFolderDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<PatientFolderDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}









