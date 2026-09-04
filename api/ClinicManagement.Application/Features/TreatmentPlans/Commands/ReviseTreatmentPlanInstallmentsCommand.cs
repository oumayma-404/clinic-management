using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Revise only the échéancier of an accepted devis — re-spreading what the patient owes without touching the
/// acts. The schedule must still sum exactly to <c>TotalPlanned</c>, and a paid installment can neither be
/// dropped nor reduced below what it has collected.
/// </summary>
public class ReviseTreatmentPlanInstallmentsCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }
    public List<InstallmentRequest> Installments { get; set; } = new();
}

public class ReviseTreatmentPlanInstallmentsCommandHandler
    : IRequestHandler<ReviseTreatmentPlanInstallmentsCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReviseTreatmentPlanInstallmentsCommandHandler> _logger;

    public ReviseTreatmentPlanInstallmentsCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IInvoiceRepository invoiceRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<ReviseTreatmentPlanInstallmentsCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _invoiceRepository = invoiceRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        ReviseTreatmentPlanInstallmentsCommand request, CancellationToken cancellationToken)
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

            /*
             * ⚠️ No blanket « ce devis est facturé » refusal — removed with the one in `AmendTreatmentPlanCommand`
             * on the owner's decision that a dentist must be able to correct anything. The divergence between a
             * corrected devis and the note raised from it is surfaced on the workspace (the note's own total
             * beside the devis') and corrected with an avoir, rather than being pre-empted by a refusal that
             * asked the dentist to reverse a numbered fiscal document in order to fix a plan.
             */
            /*
             * ⚠️ On a billed plan the échéancier collects nothing anyway — the server refuses a payment on it
             * and every installment money read drops the plan — so re-spreading one is a documentary edit. That
             * is precisely why refusing it bought nothing.
             */


            plan.ReviseInstallments(request.Installments.Select(i => (i.Id, i.DueDate, i.Amount)));
            plan.RecordAmendment();

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
            _logger.LogError(ex, "Error revising the installment schedule of plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la modification de l'échéancier.");
        }
    }
}
