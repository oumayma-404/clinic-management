using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>Restore an archived patient everywhere. Archiving is never one-way.</summary>
public class UnarchivePatientCommand : IRequest<Result<PatientDto>>
{
    public Guid Id { get; set; }
}

public class UnarchivePatientCommandHandler : IRequestHandler<UnarchivePatientCommand, Result<PatientDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UnarchivePatientCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientDto>> Handle(UnarchivePatientCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(request.Id, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientDto>.Failure("Patient introuvable.");
            }

            patient.Unarchive();

            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<PatientDto>.Success(patient.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
