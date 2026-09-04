using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>Cancel an accepted/in-progress plan (motif required). AdminOrDoctor (controller-enforced).</summary>
public class CancelTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class CancelTreatmentPlanCommandHandler : IRequestHandler<CancelTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelTreatmentPlanCommandHandler> _logger;

    public CancelTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CancelTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(CancelTreatmentPlanCommand request, CancellationToken cancellationToken)
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

            plan.Cancel(request.Reason);

            /*
             * Release any note d'honoraires this devis was attached to — the retroactive-continuation link.
             *
             * ⚠️ Without it a wrong continuation was a permanent dead end. The link is write-once, so the note
             * went on naming a cancelled devis for ever; the continuation could never be re-run for that fiche
             * (its acts still matched the « already tracked » query); and the fiche itself became undeletable,
             * because deleting it un-marks a step on a cancelled plan, which the aggregate refuses — presented
             * to the dentist as « Erreur lors de la suppression. Veuillez réessayer. »
             *
             * Detached in the SAME save as the cancellation, so the two facts can never disagree. The note's own
             * money is untouched: it keeps its lines, its number and its payments, and it simply stops speaking
             * for a devis that no longer exists.
             */
            var links = await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicResult.Value, cancellationToken);
            foreach (var link in links.Where(l => l.TreatmentPlanId == plan.Id))
            {
                var invoice = await _invoiceRepository.GetByIdAsync(link.InvoiceId, cancellationToken);
                if (invoice == null || invoice.ClinicId != clinicResult.Value)
                {
                    continue;
                }
                invoice.DetachFromTreatmentPlan(plan.Id);
                await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            }

            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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
            _logger.LogError(ex, "Error cancelling treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de l'annulation du plan de traitement.");
        }
    }
}
