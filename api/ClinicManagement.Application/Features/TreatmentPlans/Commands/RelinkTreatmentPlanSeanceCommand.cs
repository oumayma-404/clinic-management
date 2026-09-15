using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Détacher la fiche … et la rattacher à » (S6) — one press moves a recorded séance from the wrong act to the
/// right one.
///
/// <para>
/// ⚠️ <b>It was two screens and two saves, in an order that hides the answer.</b> The dentist detached on the
/// devis, then reopened the fiche and re-picked the act — and the right act was <i>invisible</i> until the
/// wrong one had been released, because the picker only offers acts nothing is recorded against. Worse, the
/// fiche's Select names <b>acts</b>, not steps, so a séance attached to the wrong <i>step</i> of the right act
/// could not be corrected there at all: it needed the booking edited on a third screen.
/// </para>
/// <para>
/// ⚠️ <b>The fiche id and the date are taken from the source before the detach</b>, which is the whole reason
/// this is one command: detaching clears the only pointer the devis has to that fiche, so a client doing it in
/// two calls has to remember the id across them — and the séance's own date has to travel too, or the
/// re-attached work would claim to have happened today.
/// </para>
/// <para>
/// ⚠️ <b>Within one devis.</b> Moving a séance to an act of a <i>different</i> plan is a different operation
/// with a different blast radius (two aggregates, two totals, two échéanciers) and is deliberately not this.
/// </para>
/// </summary>
public class RelinkTreatmentPlanSeanceCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }

    /// <summary>The act the séance is wrongly recorded against.</summary>
    public Guid FromItemId { get; set; }

    /// <summary>
    /// The step of that act, when the act has a protocol. Null means the act itself — which is what a
    /// step-less act has, and what the act-level « Détacher la fiche » releases.
    /// </summary>
    public Guid? FromStepId { get; set; }

    /// <summary>The act it should be recorded against. May be the same act when only the STEP is wrong.</summary>
    public Guid ToItemId { get; set; }

    /// <inheritdoc cref="FromStepId"/>
    public Guid? ToStepId { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class RelinkTreatmentPlanSeanceCommandHandler
    : IRequestHandler<RelinkTreatmentPlanSeanceCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RelinkTreatmentPlanSeanceCommandHandler> _logger;

    public RelinkTreatmentPlanSeanceCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<RelinkTreatmentPlanSeanceCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        RelinkTreatmentPlanSeanceCommand request, CancellationToken cancellationToken)
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

            var source = plan.Items.FirstOrDefault(i => i.Id == request.FromItemId);
            if (source == null)
            {
                return Result<TreatmentPlanDto>.Failure("Acte d'origine introuvable sur ce devis.");
            }

            var target = plan.Items.FirstOrDefault(i => i.Id == request.ToItemId);
            if (target == null)
            {
                return Result<TreatmentPlanDto>.Failure("Acte de destination introuvable sur ce devis.");
            }

            /*
             * ⚠️ **Read the fiche and the date BEFORE anything is released.** The detach clears
             * `LinkedDentalRecordId` and `DoneDate` on whatever it undoes, so reading them afterwards would
             * give null and the re-attached séance would claim to have happened today — a false clinical date
             * on the record the chart and every worklist read.
             */
            Guid? recordId;
            DateTime? doneOn;

            if (request.FromStepId is Guid fromStepId)
            {
                var step = source.Steps.FirstOrDefault(s => s.Id == fromStepId);
                if (step == null)
                {
                    return Result<TreatmentPlanDto>.Failure("Séance d'origine introuvable sur cet acte.");
                }
                recordId = step.LinkedDentalRecordId;
                doneOn = step.DoneDate;
            }
            else
            {
                recordId = source.LinkedDentalRecordId;
                doneOn = source.DoneDate;
            }

            if (doneOn is not DateTime recordedOn)
            {
                return Result<TreatmentPlanDto>.Failure(
                    "Cette séance n'est pas enregistrée : il n'y a rien à rattacher.");
            }

            if (request.FromItemId == request.ToItemId && request.FromStepId == request.ToStepId)
            {
                return Result<TreatmentPlanDto>.Failure(
                    "La séance est déjà rattachée à cet acte : choisissez-en un autre.");
            }

            // Release, then re-attach — in that order and in one transaction, because an act may not carry two
            // fiches and the target might legitimately be the same act's neighbouring step.
            if (request.FromStepId is Guid stepId)
            {
                plan.UnmarkItemStep(request.FromItemId, stepId);
            }
            else
            {
                plan.UnmarkItemDone(request.FromItemId);
            }

            if (request.ToStepId is Guid toStepId)
            {
                plan.MarkItemStepDone(request.ToItemId, toStepId, recordedOn, recordId);
            }
            else
            {
                plan.MarkItemDone(request.ToItemId, recordedOn, recordId);
            }

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Relinked séance of record {RecordId} from item {From} to item {To} on plan {PlanId}",
                recordId, request.FromItemId, request.ToItemId, plan.Id);

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
            _logger.LogError(ex, "Error relinking séance on treatment plan {PlanId}", request.PlanId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors du rattachement de la séance.");
        }
    }
}
