using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Reprendre le traitement » — the patient came back. Reopens a closed devis and restores every act parked by
/// <see cref="StopTreatmentPlanCommand"/>, each at the état its own steps derive.
/// <para>
/// ⚠️ It exists because a stopped treatment was a terminal state with no route out. « Arrêter » left the plan
/// <c>Completed</c>, which withdraws « Arrêter », « Terminer », « Facturer » and « Annuler » alike, and the
/// dropped acts had been deleted — so « Modifier le devis » could only re-type them as new ids, orphaning the
/// fiches that recorded the séances already delivered, re-quoting the act at the catalogue default rather than
/// the fee it was quoted at, and walking the plan back to « Accepté · 0 / 2 actes » two séances in.
/// </para>
/// <para>
/// The échéancier is deliberately not restored: the acts return unrealised, and re-pricing them is the amendment
/// that follows — with the dentist looking at the schedule.
/// </para>
/// </summary>
public class ReopenTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }
    public uint Version { get; set; }
}

public class ReopenTreatmentPlanCommandHandler
    : IRequestHandler<ReopenTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReopenTreatmentPlanCommandHandler> _logger;

    public ReopenTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<ReopenTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        ReopenTreatmentPlanCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            plan.Reopen();

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Reopened treatment plan {PlanId} as {Status}", plan.Id, plan.Status);

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error reopening treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la reprise du traitement.");
        }
    }
}
