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
/// « Remettre cet acte au devis » — the mirror of <see cref="WithdrawTreatmentPlanItemCommand"/>, and the
/// per-act half of <see cref="ReopenTreatmentPlanCommand"/>.
///
/// <para>
/// ⚠️ <b>The plan's own status is deliberately not re-derived.</b> Restoring one act of a <c>Stopped</c>
/// treatment does not decide the treatment is running again — « Reprendre le traitement » is the verb for
/// that, and <c>TreatmentPlan.StatusFollowsTheWork</c> states in as many words that a status a human chose may
/// not be overwritten by what the acts happen to say. The refusal that matters is the amendable window, which
/// is the aggregate's.
/// </para>
/// <para>
/// ⚠️ <b>No booking is restored either.</b> The visits the parked act had were cancelled — a slot given back
/// to the agenda has very likely been filled — so the séance is booked afresh, from the workspace, by
/// somebody looking at the diary.
/// </para>
/// </summary>
public class RestoreTreatmentPlanItemCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }
    public Guid ItemId { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class RestoreTreatmentPlanItemCommandHandler
    : IRequestHandler<RestoreTreatmentPlanItemCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RestoreTreatmentPlanItemCommandHandler> _logger;

    public RestoreTreatmentPlanItemCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<RestoreTreatmentPlanItemCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        RestoreTreatmentPlanItemCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var plan = await _planRepository.GetByIdAsync(request.PlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            // ⚠️ The clinic clock: the act's fee re-enters `TotalPlanned`, so the balance is re-spread onto a
            // Tunisian calendar day. See `TreatmentPlan.RestoreItem`.
            plan.RestoreItem(request.ItemId, ClinicClock.ClinicToday());

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Restored act {ItemId} to plan {PlanId}; total is now {Total}",
                request.ItemId, plan.Id, plan.TotalPlanned);

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
            _logger.LogError(ex, "Error restoring act {ItemId} to plan {PlanId}", request.ItemId, request.PlanId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la remise de l'acte au devis.");
        }
    }
}
