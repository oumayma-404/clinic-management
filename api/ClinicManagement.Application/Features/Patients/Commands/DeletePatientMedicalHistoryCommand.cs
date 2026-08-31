using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class DeletePatientMedicalHistoryCommand : IRequest<Result>
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
}

public class DeletePatientMedicalHistoryCommandHandler : IRequestHandler<DeletePatientMedicalHistoryCommand, Result>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public DeletePatientMedicalHistoryCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeletePatientMedicalHistoryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result.Failure("Patient introuvable.");
            }

            patient.RemoveMedicalHistoryEntry(request.Id);
            // No write to the patient row. A history entry is a child, and on this entity `UpdatedAt` shares
            // its row with the concurrency token — stamping it here refused the user’s own next save.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result.Failure(ErrorMessages.Generic, ex);
        }
    }
}


