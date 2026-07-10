using MediatR;
using ClinicManagement.Application.Common.Models;
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
                return Result.Failure("Patient not found");
            }

            patient.RemoveMedicalHistoryEntry(request.Id);
            // Update only the patient's UpdatedAt property (entry removal is automatically tracked)
            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Error deleting medical history entry: {ex.Message}");
        }
    }
}


