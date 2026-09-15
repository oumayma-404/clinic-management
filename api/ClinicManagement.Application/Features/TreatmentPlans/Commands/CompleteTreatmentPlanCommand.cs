using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Close a plan whose work is finished — the API half of « Terminer ». No UI caller (spec: the button was
/// removed and « Arrêter le traitement » took its place); the endpoint and the automatic clôture stay.
/// <para>
/// ⚠️ <b>It refuses a plan with acts still « non réalisé », and that refusal is deliberate.</b> It used to pass
/// <c>leaveUnrealisedActs: true</c>, closing the plan over unfinished work and leaving the échéancier untouched
/// — so the patient went on owing for séances nobody would ever do, on a devis badged « Terminé » where every
/// remedy had been withdrawn. That case belongs to <c>StopTreatmentPlanCommand</c>, which parks the unrealised
/// acts and re-spreads the balance onto what was kept, in one transition.
/// </para>
/// <para>
/// Money is untouched either way: « Terminé » means the work is over, not that the patient has paid.
/// </para>
/// </summary>
public class CompleteTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class CompleteTreatmentPlanCommandHandler : IRequestHandler<CompleteTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CompleteTreatmentPlanCommandHandler> _logger;

    public CompleteTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CompleteTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(CompleteTreatmentPlanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            // Tenant isolation: a plan from another clinic reads as "not found".
            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            /*
             * ⚠️ `leaveUnrealisedActs` is NOT passed any more, and dropping it is the fix — « Arrêter le
             * traitement » owns that case now.
             *
             * Closing a plan with acts still « non réalisé » left them so AND left the échéancier alone, so the
             * patient went on owing for work nobody would ever do: a live créance on a devis badged « Terminé »,
             * outside every remedy (« Arrêter » is withdrawn once the plan is closed). `StopTreatment` parks the
             * unrealised acts, re-spreads the balance onto what was kept and closes the plan in one transition —
             * which is what this endpoint's confirmation was describing all along.
             *
             * ⚠️ The endpoint and the automatic path are unchanged (spec AC-11 / API contract): the automatic
             * clôture fires when the last step lands, so every act really is done and the default refuses
             * nothing. What is gone is the API-only way to close a plan over unfinished work.
             */
            plan.Complete();

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error completing treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la clôture du plan de traitement.");
        }
    }
}
