using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Detach one <b>step</b> of a planned act from the fiche de soins that evidenced it, returning it to « à venir ».
/// <para>
/// The step-level twin of <see cref="UnmarkTreatmentPlanItemDoneCommand"/>, and the implementation of the
/// « détachez-la de cette fiche » that <c>TreatmentPlanItemStep.MarkDone</c> tells the user to perform. Without it
/// one step ticked against the wrong séance would be permanent — and because the last step landing completes the
/// act, and the last act completes the plan, it could close a whole devis with no way back.
/// </para>
/// <para>
/// ⚠️ It <b>reuses the act-level billing guard verbatim</b> rather than writing a step-shaped one. « This work is
/// already billed » has to mean the same thing at both granularities, and the two routes money reaches are
/// identical: the devis→facture bridge bills the whole plan, and a fiche line bills the séance — which for a
/// stepped act is precisely this step's own fiche. A second copy of that rule is how one granularity quietly
/// becomes correctable while the other is not.
/// </para>
/// </summary>
public class UnmarkTreatmentPlanItemStepCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }
    public Guid ItemId { get; set; }
    public Guid StepId { get; set; }
}

public class UnmarkTreatmentPlanItemStepCommandHandler
    : IRequestHandler<UnmarkTreatmentPlanItemStepCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UnmarkTreatmentPlanItemStepCommandHandler> _logger;

    public UnmarkTreatmentPlanItemStepCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IInvoiceRepository invoiceRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<UnmarkTreatmentPlanItemStepCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _invoiceRepository = invoiceRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        UnmarkTreatmentPlanItemStepCommand request, CancellationToken cancellationToken)
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

            var item = plan.Items.FirstOrDefault(i => i.Id == request.ItemId);
            if (item == null)
            {
                return Result<TreatmentPlanDto>.Failure("Acte introuvable.");
            }

            var step = item.Steps.FirstOrDefault(s => s.Id == request.StepId);
            if (step == null)
            {
                return Result<TreatmentPlanDto>.Failure("Étape introuvable.");
            }

            // Say so rather than silently succeeding — a step never marked réalisée has nothing to detach, and
            // reporting success is the same class of lie this feature exists to remove.
            if (!step.IsDone)
            {
                return Result<TreatmentPlanDto>.Failure("Cette étape n'est pas marquée comme réalisée.");
            }

            var billedCheck = await EnsureNotBilledAsync(plan, step, clinicResult.Value, cancellationToken);
            if (billedCheck.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(billedCheck.Error!);
            }

            plan.UnmarkItemStep(request.ItemId, request.StepId);

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
            _logger.LogError(
                ex, "Error un-marking step {StepId} of item {ItemId} on plan {PlanId}",
                request.StepId, request.ItemId, request.PlanId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la correction de l'étape.");
        }
    }

    /// <summary>
    /// Refuse when a live invoice already bills this work — the same two routes
    /// <see cref="UnmarkTreatmentPlanItemDoneCommandHandler"/> checks, with the step's own fiche standing in for
    /// the act's.
    /// </summary>
    private async Task<Result> EnsureNotBilledAsync(
        TreatmentPlan plan, TreatmentPlanItemStep step, Guid clinicId, CancellationToken cancellationToken)
    {
        var planLinks = await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken);
        if (PlanBillingRules.BilledPlanIds(planLinks).Contains(plan.Id))
        {
            return Result.Failure(
                "Ce devis est déjà facturé. Annulez la facture (ou émettez un avoir) avant de corriger une étape.");
        }

        // No fiche attached ⇒ nothing an invoice line could be billing for this step.
        if (step.LinkedDentalRecordId is not { } recordId)
        {
            return Result.Success();
        }

        var recordLinks = await _invoiceRepository.GetDentalRecordLinksAsync(clinicId, cancellationToken);
        var billing = recordLinks.FirstOrDefault(l =>
            l.DentalRecordId == recordId && PlanBillingRules.RepresentsItsPlan(l.Status));

        return billing.InvoiceId == Guid.Empty
            ? Result.Success()
            : Result.Failure(
                $"La fiche de soins de cette étape est facturée sur la note d'honoraires {billing.Number}. "
                + "Annulez la facture (ou émettez un avoir) avant de corriger l'étape.");
    }
}
