using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Mettre cet acte de côté » — the patient is not having this one, and everything else about the treatment
/// stays exactly as it is.
///
/// <para>
/// ⚠️ <b>This is the capability the driving complaint was asking for</b> — « once something becomes a
/// treatment plan, I am always stuck ». Removing an act with delivered work is refused, correctly, and the
/// remedy that refusal named was a <b>three-deep chain</b> it did not disclose: détacher la fiche → refused by
/// <c>DentalRecordBillingGuard</c> if the fiche is on a live note → whose own remedy (annuler / avoir) is
/// refused if the note has a live payment. Parking needs none of it: the fiche links are preserved, the act
/// simply leaves <c>TotalPlanned</c> and the échéancier is re-spread onto what is left.
/// </para>
/// <para>
/// ⚠️ Parking was all-or-nothing before, in both directions — <c>StopTreatment</c> parks every undelivered
/// act and <c>Reopen</c> restores every one — so « la patiente revient, mais seulement pour la couronne »
/// forced reopening the whole treatment and re-inflating the total with the implants she had declined.
/// </para>
/// <para>
/// The visits booked for the act are released like an amendment's (<see cref="PlanBookingRelease"/>): the
/// séance is not happening, so a visit left with nothing else to do is cancelled after the save.
/// </para>
/// </summary>
public class WithdrawTreatmentPlanItemCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }
    public Guid ItemId { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class WithdrawTreatmentPlanItemCommandHandler
    : IRequestHandler<WithdrawTreatmentPlanItemCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WithdrawTreatmentPlanItemCommandHandler> _logger;

    public WithdrawTreatmentPlanItemCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        IMediator mediator,
        IUnitOfWork unitOfWork,
        ILogger<WithdrawTreatmentPlanItemCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        WithdrawTreatmentPlanItemCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plan = await _planRepository.GetByIdAsync(request.PlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            // Refuses the last active act (« arrêtez le traitement plutôt ») and re-spreads the échéancier,
            // which is where the « déjà encaissé » rule fires. See `TreatmentPlan.WithdrawItem`.
            plan.WithdrawItem(request.ItemId, ClinicClock.ClinicToday());

            var appointmentsToCancel = await PlanBookingRelease.ReleaseAsync(
                new[] { request.ItemId }, clinicId, _appointmentRepository, cancellationToken);

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // After the save and through MediatR — the amend handler's two reasons, verbatim.
            foreach (var appointmentId in appointmentsToCancel)
            {
                var cancelled = await _mediator.Send(
                    new UpdateAppointmentCommand
                    {
                        Id = appointmentId,
                        Status = nameof(AppointmentStatus.Cancelled),
                        CancellationReason = "Acte mis de côté",
                    },
                    cancellationToken);

                if (!cancelled.IsSuccess)
                {
                    _logger.LogWarning(
                        "Act {ItemId} was parked but appointment {AppointmentId} could not be cancelled: {Error}",
                        request.ItemId, appointmentId, cancelled.Error);
                }
            }

            _logger.LogInformation(
                "Parked act {ItemId} of plan {PlanId}; kept total {Total}",
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
            _logger.LogError(ex, "Error parking act {ItemId} of plan {PlanId}", request.ItemId, request.PlanId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la mise de côté de l'acte.");
        }
    }
}
