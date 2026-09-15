using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Rétablir ce devis annulé » — the way out of the one state nothing in the product could leave.
///
/// <para>
/// ⚠️ <b><c>Cancelled</c> was absorbing.</b> Every guard on the aggregate excludes it — amend, correct, pay,
/// reorder, set steps, void a payment — and <c>CanBeDeleted</c> is Draft-only, so a cancelled devis could not
/// even be destroyed. The workspace confirmed it on screen: « Devis PDF » was the only surviving control, with
/// no explanation and no route. And the stop button can reach <c>Cancel</c> <i>without the dentist choosing
/// it</i>, so a mis-click on « Arrêter le traitement » was permanent.
/// </para>
/// <para>
/// The number is untouched (the series stays gapless) and the original motif is kept — it explains a document
/// that may be in a patient's hands, and un-cancelling does not make it untrue. See
/// <c>TreatmentPlan.Uncancel</c>.
/// </para>
/// </summary>
public class UncancelTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }

    /// <summary>Why it is being brought back. Required, and appended to the cancellation motif.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class UncancelTreatmentPlanCommandHandler
    : IRequestHandler<UncancelTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UncancelTreatmentPlanCommandHandler> _logger;

    public UncancelTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<UncancelTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        UncancelTreatmentPlanCommand request, CancellationToken cancellationToken)
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

            // ⚠️ The clinic clock, never `DateTime.Today` — the plan carries debt again the instant the status
            // is written, so the re-spread échéance is a calendar day in Tunisia.
            plan.Uncancel(request.Reason, ClinicClock.ClinicToday());

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Uncancelled treatment plan {PlanId} as {Status}", plan.Id, plan.Status);

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error uncancelling treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors du rétablissement du devis.");
        }
    }
}
