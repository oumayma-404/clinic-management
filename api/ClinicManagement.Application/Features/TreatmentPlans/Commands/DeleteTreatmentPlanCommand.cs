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
    // Optional for the older construction sites; the DI container always supplies them.
    private readonly IAppointmentRepository? _appointmentRepository;
    private readonly ISender? _sender;

    public DeleteTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeleteTreatmentPlanCommandHandler> logger,
        IAppointmentRepository? appointmentRepository = null,
        ISender? sender = null)
    {
        _appointmentRepository = appointmentRepository;
        _sender = sender;
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

            /*
              * ⚠️ « … il doit être annulé » sent the dentist to the one IRREVERSIBLE action without saying
              * so, and it fired identically on a closed treatment — where cancelling would freeze a
              * delivered-work record for ever — and on a live one, where « Annuler le devis » is not even on
              * screen (the capability is a branch of « Arrêter le traitement », reachable only while nothing
              * has been delivered). Two refusals, because they name two different next steps.
              */
            if (plan.Status is TreatmentPlanStatus.Completed or TreatmentPlanStatus.Stopped
                or TreatmentPlanStatus.Cancelled)
            {
                return Result.Failure(
                    "Ce traitement est clôturé : il se conserve et ne peut plus être supprimé.");
            }

            if (!plan.CanBeDeleted)
            {
                return Result.Failure(
                    "Un devis accepté ne peut pas être supprimé : utilisez « Arrêter le traitement », "
                    + "qui conserve ce qui a déjà été fait.");
            }

            /*
             * ⚠️ The séances booked on it are let go FIRST. There is no FK from a visit to a devis act (the
             * links are bare columns, deliberately), so deleting the plan used to leave its visits booked —
             * reminded, chased by the worklist — pointing at acts that no longer exist.
             */
            var emptied = _appointmentRepository is null
                ? new List<Guid>()
                : await PlanBookingRelease.ReleaseAsync(
                    plan.Items.Select(i => i.Id).ToList(), clinicResult.Value, _appointmentRepository,
                    cancellationToken);

            await _planRepository.DeleteAsync(plan.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_sender is not null)
            {
                await PlanBookingRelease.CancelEmptiedAsync(
                    _sender, emptied, "Traitement supprimé", _logger, cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error deleting treatment plan {PlanId}", request.Id);
            return Result.Failure("Erreur lors de la suppression du plan de traitement.");
        }
    }
}
