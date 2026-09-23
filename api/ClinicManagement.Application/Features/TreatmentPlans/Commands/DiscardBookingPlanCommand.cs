using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Undo a treatment a booking dialog created on save when the visit itself was never created — the slot was
/// taken, the hour was closed, or the user pressed « Retour » on the confirmation.
/// </summary>
/// <remarks>
/// <para>
/// A Draft (« suivre ce traitement ») is deleted; a numbered devis (« c'est la suite d'une séance précédente »)
/// is cancelled, so its number stays in the sequence and any note it was attached to is released
/// (<see cref="CancelTreatmentPlanCommand"/>). Before this, both stayed behind — the numbered one with a live
/// créance for a séance nobody booked.
/// </para>
/// <para>
/// ⚠️ <b>`AnyClinicRole` and therefore narrow on purpose.</b> Only a plan that is minutes old, has no visit
/// booked on any of its acts and holds no money qualifies — exactly what the dialog just produced, and nothing a
/// colleague has started working with.
/// </para>
/// </remarks>
public class DiscardBookingPlanCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class DiscardBookingPlanCommandHandler : IRequestHandler<DiscardBookingPlanCommand, Result>
{
    /// <summary>How long after creation a plan still counts as « what this dialog just made ».</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromHours(2);

    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ISender _sender;
    private readonly ILogger<DiscardBookingPlanCommandHandler> _logger;

    public DiscardBookingPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        ISender sender,
        ILogger<DiscardBookingPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _sender = sender;
        _logger = logger;
    }

    public async Task<Result> Handle(DiscardBookingPlanCommand request, CancellationToken cancellationToken)
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

            if (plan.Status == TreatmentPlanStatus.Cancelled)
            {
                return Result.Success();
            }

            if (DateTime.UtcNow - plan.CreatedAt > Window
                || plan.HasReceipts
                || plan.Status is not (TreatmentPlanStatus.Draft or TreatmentPlanStatus.Accepted
                    or TreatmentPlanStatus.InProgress))
            {
                return Result.Failure("Ce traitement est déjà en usage : il ne peut pas être retiré ici.");
            }

            var booked = await _appointmentRepository.GetByTreatmentPlanItemIdsAsync(
                clinicResult.Value, plan.Items.Select(i => i.Id).ToList(), cancellationToken);
            if (booked.Count > 0)
            {
                return Result.Failure("Un rendez-vous est déjà planifié sur ce traitement : il ne peut pas être retiré ici.");
            }

            if (plan.Status == TreatmentPlanStatus.Draft)
            {
                return await _sender.Send(new DeleteTreatmentPlanCommand { Id = plan.Id }, cancellationToken);
            }

            var cancelled = await _sender.Send(
                new CancelTreatmentPlanCommand { Id = plan.Id, Reason = "Rendez-vous non créé" }, cancellationToken);
            return cancelled.IsFailure ? Result.Failure(cancelled.Error ?? "Le devis n'a pas pu être annulé.") : Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error discarding booking plan {PlanId}", request.Id);
            return Result.Failure("Le traitement n'a pas pu être retiré.");
        }
    }
}
