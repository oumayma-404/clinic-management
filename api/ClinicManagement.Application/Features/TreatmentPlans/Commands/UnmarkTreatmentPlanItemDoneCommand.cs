using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Undo <see cref="MarkTreatmentPlanItemDoneCommand"/>: return one act to « prévu » and detach the fiche de soins
/// that evidenced it.
/// <para>
/// This is the operation <c>TreatmentPlanItem.MarkDone</c> has always told the user to perform — « détachez-le de
/// cette fiche » — and that existed nowhere in the domain, application, API or UI. Because marking the last act done
/// auto-completes the plan, one act ticked against the wrong fiche closed the whole devis with no way back.
/// </para>
/// </summary>
public class UnmarkTreatmentPlanItemDoneCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }
    public Guid ItemId { get; set; }
}

public class UnmarkTreatmentPlanItemDoneCommandHandler
    : IRequestHandler<UnmarkTreatmentPlanItemDoneCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UnmarkTreatmentPlanItemDoneCommandHandler> _logger;

    public UnmarkTreatmentPlanItemDoneCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<UnmarkTreatmentPlanItemDoneCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _invoiceRepository = invoiceRepository;
        _creditNoteRepository = creditNoteRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        UnmarkTreatmentPlanItemDoneCommand request, CancellationToken cancellationToken)
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

            // Say so rather than silently succeeding — an act that was never marked réalisé has nothing to
            // detach, and reporting success would be the same class of lie this whole feature exists to remove.
            if (item.Status != TreatmentPlanItemStatus.Done)
            {
                return Result<TreatmentPlanDto>.Failure("Cet acte n'est pas marqué comme réalisé.");
            }

            // Un-marking work a live invoice already bills would leave the plan saying « prévu » while the money
            // says it was carried out. The aggregate cannot check this — it holds no invoice reference — and the
            // work can be billed two independent ways, so both are checked.
            var billedCheck = await EnsureNotBilledAsync(plan, item, clinicResult.Value, cancellationToken);
            if (billedCheck.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(billedCheck.Error!);
            }

            plan.UnmarkItemDone(request.ItemId);

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
            _logger.LogError(ex, "Error un-marking item {ItemId} on plan {PlanId}", request.ItemId, request.PlanId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la correction de l'acte.");
        }
    }

    /// <summary>
    /// Refuse when a live invoice already bills this work. Realised work reaches an invoice by **two independent
    /// routes**, and checking only one leaves the other silently correctable:
    /// <list type="number">
    /// <item>the devis→facture bridge — one invoice representing the whole plan. Checked with the guard
    /// <c>AmendTreatmentPlanCommand</c> uses, deliberately the same rule and wording, because "the plan is billed"
    /// must mean one thing across every mutation;</item>
    /// <item>a line billing the act's own fiche de soins (<c>InvoiceLine.DentalRecordId</c>), with no bridge
    /// invoice in sight — the case a plan-level check cannot see at all.</item>
    /// </list>
    /// Both use light projections rather than <c>GetFilteredAsync</c>, which would load every invoice of the
    /// clinic with its lines and payments to answer a membership question.
    /// </summary>
    private async Task<Result> EnsureNotBilledAsync(
        TreatmentPlan plan, TreatmentPlanItem item, Guid clinicId, CancellationToken cancellationToken)
    {
        /*
         * ⚠️ The blanket « ce devis est facturé » refusal was here and is gone — see `AmendTreatmentPlanCommand`.
         * What remains is the check that carries weight: whether THIS act's own fiche de soins is billed on a
         * live note. That one names the note, is about the very work being detached, and is what stops a réalisé
         * act quietly losing the evidence an invoice line is charging for.
         *
         * ⚠️ It is `DentalRecordBillingGuard`'s now, and it was written here — with `RepresentsItsPlan(status)`
         * over the light link projection, which **cannot see an avoir**. A credit note is a separate aggregate;
         * issuing one leaves the invoice `Paid`. So the refusal told the dentist to annuler ou émettre un avoir,
         * they issued the avoir, retried, and got the identical sentence — while « annuler » was refused too,
         * because a note with a live payment cannot be cancelled. Both named remedies were unreachable and the
         * wrongly-attached fiche was permanent.
         */
        return await DentalRecordBillingGuard.EnsureWorkIsNotBilledAsync(
            _invoiceRepository,
            _creditNoteRepository,
            clinicId,
            item.LinkedDentalRecordId,
            "cet acte",
            cancellationToken);
    }
}
