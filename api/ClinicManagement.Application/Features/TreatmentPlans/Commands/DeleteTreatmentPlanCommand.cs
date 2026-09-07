using ClinicManagement.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>Delete a draft treatment plan. Only a draft can be deleted (an accepted plan is cancelled).</summary>
public class DeleteTreatmentPlanCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class DeleteTreatmentPlanCommandHandler : IRequestHandler<DeleteTreatmentPlanCommand, Result>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteTreatmentPlanCommandHandler> _logger;

    public DeleteTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeleteTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeleteTreatmentPlanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result.Failure("Plan de traitement introuvable.");
            }

            /*
             * ⚠️ Two refusals, because they name two different next steps and one sentence for both sent the
             * dentist to the wrong screen. « Un plan accepté … doit être annulé » is right for a numbered devis
             * and wrong for a followed treatment with séances on it — that one is not annulé (a Draft has no
             * number to void) but *arrêté*, which keeps the work that was done. See `TreatmentPlan.CanBeDeleted`.
             */
            if (plan.Status == TreatmentPlanStatus.Draft && !plan.CanBeDeleted)
            {
                return Result.Failure(
                    "Des séances ont déjà été réalisées sur ce traitement : il ne peut plus être supprimé. "
                    + "Utilisez « Arrêter le traitement » pour le clôturer en conservant ce qui a été fait.");
            }

            if (!plan.CanBeDeleted)
            {
                return Result.Failure("Un plan accepté ne peut pas être supprimé ; il doit être annulé.");
            }

            await _planRepository.DeleteAsync(plan.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error deleting treatment plan {PlanId}", request.Id);
            return Result.Failure("Erreur lors de la suppression du plan de traitement.");
        }
    }
}
