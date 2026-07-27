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
/// Amend an accepted / in-progress devis: add acts, remove acts, and revise the échéancier — in one call, so
/// the schedule can never be left out of sync with a total that just changed.
/// <para>
/// Before this, a plan froze at acceptance and the only way to change treatment was Cancel + retype, losing
/// the devis number, the échéancier and every réalisé act.
/// </para>
/// </summary>
public class AmendTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }
    public List<TreatmentPlanItemRequest> AddItems { get; set; } = new();
    public List<Guid> RemoveItemIds { get; set; } = new();

    /// <summary>
    /// The full replacement échéancier. May be omitted when the amendment leaves <c>TotalPlanned</c>
    /// unchanged; **required** when it changes, otherwise the schedule and the total would disagree and the
    /// two formulas the money reads use would stop matching.
    /// </summary>
    public List<InstallmentRequest> Installments { get; set; } = new();
}

public class AmendTreatmentPlanCommandHandler : IRequestHandler<AmendTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IDentalActCodeRepository _dentalActRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AmendTreatmentPlanCommandHandler> _logger;

    public AmendTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IInvoiceRepository invoiceRepository,
        IAppointmentRepository appointmentRepository,
        IDentalActCodeRepository dentalActRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<AmendTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _invoiceRepository = invoiceRepository;
        _appointmentRepository = appointmentRepository;
        _dentalActRepository = dentalActRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(AmendTreatmentPlanCommand request, CancellationToken cancellationToken)
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

            if (request.AddItems.Count == 0 && request.RemoveItemIds.Count == 0 && request.Installments.Count == 0)
            {
                return Result<TreatmentPlanDto>.Failure("Aucune modification demandée.");
            }

            var billedGuard = await EnsureNotBilledAsync(plan, clinicId, cancellationToken);
            if (billedGuard.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(billedGuard.Error!);
            }

            var totalBefore = plan.TotalPlanned;

            // Removals first: taking an act out lowers the total, and doing it before the additions keeps
            // the appended acts' sequence numbers contiguous.
            if (request.RemoveItemIds.Count > 0)
            {
                var liveByItemId = await LiveAppointmentsByItemAsync(plan, clinicId, cancellationToken);
                foreach (var itemId in request.RemoveItemIds)
                {
                    liveByItemId.TryGetValue(itemId, out var liveAt);
                    plan.RemoveItem(itemId, liveAt);
                }
            }

            if (request.AddItems.Count > 0)
            {
                var items = await TreatmentPlanItemPricing.ResolveAsync(
                    request.AddItems, clinicId, _dentalActRepository, cancellationToken);
                plan.AddItems(items);
            }

            // A changed total MUST come with a schedule: leaving the old one would break
            // Σ installment.Amount == TotalPlanned, the invariant that keeps « Solde patient » and
            // « Créances » reporting the same number.
            if (plan.TotalPlanned != totalBefore && request.Installments.Count == 0)
            {
                return Result<TreatmentPlanDto>.Failure(
                    "Le total du devis a changé : renvoyez l'échéancier correspondant au nouveau total.");
            }

            if (request.Installments.Count > 0)
            {
                plan.ReviseInstallments(request.Installments.Select(i => (i.Id, i.DueDate, i.Amount)));
            }

            // One call, one revision — adding an act *and* re-spreading the échéancier is a single amendment
            // from the patient's point of view, and « révision N » only means something if it counts the
            // edits they could have been handed a printout of.
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
            _logger.LogError(ex, "Error amending treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la modification du devis.");
        }
    }

    /// <summary>
    /// A plan already represented by a real invoice cannot be amended. This is a **correctness** guard, not a
    /// convenience one: every money read counts such a plan through its invoice, and the invoice's lines froze
    /// at issue with no re-sync command anywhere — so acts added afterwards would be invisible in every
    /// balance. A silent undercount is exactly the bug class the unified money reads exist to prevent.
    /// The escape hatch already exists (cancel the invoice while unpaid, or issue an avoir once paid), and a
    /// plan whose only bridge is cancelled is amendable again.
    /// <para>
    /// Lives in the handler rather than the aggregate because <c>TreatmentPlan</c> holds no invoice reference.
    /// </para>
    /// </summary>
    private async Task<Result> EnsureNotBilledAsync(TreatmentPlan plan, Guid clinicId, CancellationToken cancellationToken)
    {
        var links = await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken);
        var billed = PlanBillingRules.BilledPlanIds(links);

        return billed.Contains(plan.Id)
            ? Result.Failure("Ce devis est déjà facturé. Annulez la facture (ou émettez un avoir) avant de modifier le plan.")
            : Result.Success();
    }

    /// <summary>
    /// The still-standing appointment (if any) for each of the plan's acts, so <c>RemoveItem</c> can refuse to
    /// strand a patient who is booked for work that would no longer exist. One batched read for the whole plan.
    /// </summary>
    private async Task<Dictionary<Guid, DateTime?>> LiveAppointmentsByItemAsync(
        TreatmentPlan plan, Guid clinicId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var itemIds = plan.Items.Select(i => i.Id).ToList();
        var workflow = await TreatmentPlanWorkflowProjection.BuildAsync(
            new[] { plan }, clinicId, _appointmentRepository, _invoiceRepository, now, cancellationToken);

        // Only a *future* booking blocks removal — that is the case the guard is about: a patient still
        // expected, with reminders already sent, for work that would no longer exist. A past appointment is
        // history; its plan link simply stops resolving, which is harmless because the derivation runs
        // act → appointment, never the reverse. (A cancelled or no-show appointment is already excluded by
        // the projection, so cancelling the RDV is what unblocks the removal.)
        return itemIds.ToDictionary(
            id => id,
            id => workflow.ScheduledByItemId.TryGetValue(id, out var appointment)
                  && appointment.AppointmentDateTime >= now
                ? (DateTime?)appointment.AppointmentDateTime
                : null);
    }
}
