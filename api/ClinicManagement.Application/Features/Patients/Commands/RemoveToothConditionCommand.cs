using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// Remove a charted diagnosis from a patient's odontogram. Only <see cref="ToothStateSource.Diagnosis"/>
/// entries can be removed here; treatment entries are owned by their dental record and are edited through it.
/// </summary>
public class RemoveToothConditionCommand : IRequest<Result>
{
    public Guid PatientId { get; set; }
    public Guid ToothStateId { get; set; }
}

public class RemoveToothConditionCommandHandler : IRequestHandler<RemoveToothConditionCommand, Result>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IToothStateRepository _toothStateRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveToothConditionCommandHandler(
        IPatientRepository patientRepository,
        IToothStateRepository toothStateRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _toothStateRepository = toothStateRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveToothConditionCommand request, CancellationToken cancellationToken)
    {
        var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
        if (clinicResult.IsFailure)
        {
            return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
        }

        var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
        if (patient == null || patient.ClinicId != clinicResult.Value)
        {
            return Result.Failure("Patient introuvable.");
        }

        var state = await _toothStateRepository.GetByIdAsync(request.ToothStateId, cancellationToken);
        if (state == null || state.PatientId != request.PatientId)
        {
            return Result.Failure("Diagnostic introuvable.");
        }
        if (state.Source != ToothStateSource.Diagnosis)
        {
            return Result.Failure("Seul un diagnostic peut être retiré ici ; un acte réalisé se modifie via sa fiche.");
        }

        await _toothStateRepository.DeleteAsync(state.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
