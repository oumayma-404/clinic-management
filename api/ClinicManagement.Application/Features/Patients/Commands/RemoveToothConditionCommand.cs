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
    /// <summary>
    /// The refusal that is a <b>rule</b> and not a missing row: the state exists, and it came from a treatment.
    ///
    /// <para>⚠️ The controller passed <c>404</c> as the fallback for every failure code, so this answered « Not
    /// Found » about a row it had just read — a client branching on the status would conclude the state was gone
    /// and drop it from the chart. No user impact today only because the UI never issues it for such a row and
    /// renders the body rather than the status; both of those are true by accident.</para>
    /// </summary>
    public const string NotADiagnosisCode = "tooth_state_not_a_diagnosis";

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
            // Carries a code so the controller can answer 400 for a RULE refusal — the row exists and was found,
            // which is the one thing a 404 asserts is false.
            return Result.Failure(
                "Seul un diagnostic peut être retiré ici ; un acte réalisé se modifie via sa fiche.",
                RemoveToothConditionCommand.NotADiagnosisCode);
        }

        await _toothStateRepository.DeleteAsync(state.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
