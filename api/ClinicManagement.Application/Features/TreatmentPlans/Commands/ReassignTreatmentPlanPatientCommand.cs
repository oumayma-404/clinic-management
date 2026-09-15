using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Move a devis to the patient it was actually for.
///
/// <para>
/// ⚠️ <b>A devis on the wrong patient was unfixable.</b> <c>PatientId</c> is constructor-only on the
/// aggregate, the field is a disabled input in every edit mode, and deletion works only for a Draft with no
/// work — so the way out was to retype everything under a <b>new number</b>, spending a second one out of a
/// per-clinic-per-year series to fix a mis-click in a picker.
/// </para>
/// <para>
/// Two refusals, and they sit in different places because they are different questions.
/// <b>Delivered work</b> is the aggregate's (<c>TreatmentPlan.ReassignPatient</c>): a recorded séance belongs
/// to the mouth it was carried out in, and moving the devis under it would re-file another patient's clinical
/// history. A <b>linked note d'honoraires</b> is this handler's, for the reason every billing rule is — the
/// aggregate holds no invoice reference — and it is not negotiable either: the note names a patient on a
/// numbered fiscal document, so the devis behind it cannot quietly name a different one.
/// </para>
/// <para>
/// The visits booked for the plan's acts are released the way an amendment releases them
/// (<see cref="PlanBookingRelease"/>): they were booked into the wrong patient's file, and a visit left with
/// nothing else to do is cancelled after the save, through <c>UpdateAppointmentCommand</c> so the
/// notification, the reminders and the calendar event all go with it.
/// </para>
/// </summary>
public class ReassignTreatmentPlanPatientCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }

    /// <summary>The patient this devis was really for.</summary>
    public Guid PatientId { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class ReassignTreatmentPlanPatientCommandHandler
    : IRequestHandler<ReassignTreatmentPlanPatientCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IMediator _mediator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReassignTreatmentPlanPatientCommandHandler> _logger;

    public ReassignTreatmentPlanPatientCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IInvoiceRepository invoiceRepository,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        IMediator mediator,
        IUnitOfWork unitOfWork,
        ILogger<ReassignTreatmentPlanPatientCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _invoiceRepository = invoiceRepository;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _mediator = mediator;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        ReassignTreatmentPlanPatientCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Patient introuvable.");
            }

            if (plan.PatientId == request.PatientId)
            {
                return Result<TreatmentPlanDto>.Success(plan.ToDto(patient.GetFullName()));
            }

            /*
             * The invoice half of the refusal — the aggregate cannot see it. A note that names this devis
             * names a patient on a numbered fiscal document, and moving the devis under a different one would
             * leave the two disagreeing with nothing to reconcile them. A CANCELLED note is void and does not
             * hold the devis, which is the recovery path: cancel the note, then move the devis.
             */
            var noteNumbers = (await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken))
                .Where(l => l.TreatmentPlanId == plan.Id && l.Status != InvoiceStatus.Cancelled)
                .Select(l => l.Number ?? "brouillon")
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            if (noteNumbers.Count > 0)
            {
                return Result<TreatmentPlanDto>.Failure(
                    $"Ce devis est rattaché à une note d'honoraires ({string.Join(", ", noteNumbers)}) : "
                    + "détachez-la de ce traitement avant de changer de patient.");
            }

            // Refuses on delivered work, and does nothing else — see `TreatmentPlan.ReassignPatient`.
            plan.ReassignPatient(request.PatientId);

            // The visits were booked into the wrong patient's file, so every act of the plan is released.
            var appointmentsToCancel = await PlanBookingRelease.ReleaseAsync(
                plan.Items.Select(i => i.Id).ToList(), clinicId, _appointmentRepository, cancellationToken);

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            /*
             * After the save, and through MediatR — the amend handler's reasoning verbatim:
             * `UpdateAppointmentCommandHandler` saves on this same scoped DbContext, so sending it earlier
             * would commit the plan's edit without the expected-version check; and cancelling a visit is five
             * things (the notification, the review row, the unsent reminders, the calendar event and the
             * broadcast), none of which a hand-rolled cancel here would do.
             */
            foreach (var appointmentId in appointmentsToCancel)
            {
                var cancelled = await _mediator.Send(
                    new UpdateAppointmentCommand
                    {
                        Id = appointmentId,
                        Status = nameof(AppointmentStatus.Cancelled),
                        CancellationReason = "Devis transféré à un autre patient",
                    },
                    cancellationToken);

                if (!cancelled.IsSuccess)
                {
                    _logger.LogWarning(
                        "Plan {PlanId} moved patient but appointment {AppointmentId} could not be cancelled: {Error}",
                        plan.Id, appointmentId, cancelled.Error);
                }
            }

            _logger.LogInformation("Treatment plan {PlanId} reassigned to patient {PatientId}", plan.Id, plan.PatientId);

            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient.GetFullName()));
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
            _logger.LogError(ex, "Error reassigning treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors du changement de patient.");
        }
    }
}
