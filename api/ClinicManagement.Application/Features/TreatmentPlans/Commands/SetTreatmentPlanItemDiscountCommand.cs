using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Accorder une remise » on one act of a devis (S2), or clear one by sending 0.
///
/// <para>
/// ⚠️ <b>The only way to give a discount was to overtype the tarif</b>, which loses the fact that a discount
/// was given at all: the devis then prints as though the reduced figure were the price, and the practice
/// cannot answer « combien avons-nous offert cette année ? ». The act keeps its tarif now and the reduction is
/// a line of its own — on the devis PDF, in the DTO, and in <c>TreatmentPlan.TotalDiscount</c>.
/// </para>
/// <para>
/// ⚠️ <b>It moves money</b>, which is why it is gated like a price change rather than like setting the steps:
/// <c>TotalPlanned</c> sums the <b>net</b>, so the échéancier is re-spread, <c>RevisionNumber</c> is bumped
/// (a patient holding the earlier printout signed for a different total) and the <c>TotalPlanned ≥ AmountPaid</c>
/// invariant is checked by <c>RespreadSchedule</c> — a remise that would put the total below what has already
/// been collected is refused by name.
/// </para>
/// </summary>
public class SetTreatmentPlanItemDiscountCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }
    public Guid ItemId { get; set; }

    /// <summary>The remise in dinars. 0 clears it; the aggregate refuses more than the act's tarif.</summary>
    public decimal DiscountAmount { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }

    /// <summary>
    /// Set only after the screen asked « rendre X DT au patient ? »: how the difference is given back today
    /// (<see cref="PlanRefund"/>). Absent, a total below what was collected is refused with <see cref="PlanRefund.Code"/>.
    /// </summary>
    public string? RefundMethod { get; set; }
}

public class SetTreatmentPlanItemDiscountCommandHandler
    : IRequestHandler<SetTreatmentPlanItemDiscountCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetTreatmentPlanItemDiscountCommandHandler> _logger;

    public SetTreatmentPlanItemDiscountCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<SetTreatmentPlanItemDiscountCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        SetTreatmentPlanItemDiscountCommand request, CancellationToken cancellationToken)
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

            // ⚠️ The clinic clock, never `DateTime.Today` — the re-spread échéance is a calendar day in Tunisia.
            if (!PlanRefund.TryParse(request.RefundMethod, out var refundMethod, out var methodError))
            {
                return Result<TreatmentPlanDto>.Failure(methodError!);
            }
            try
            {
                plan.SetItemDiscount(request.ItemId, request.DiscountAmount, ClinicClock.ClinicToday(), refundMethod);
            }
            catch (InvalidOperationException) when (PlanRefund.IsNeeded(plan, refundMethod))
            {
                return Result<TreatmentPlanDto>.Failure(PlanRefund.Sentence(plan), PlanRefund.Code);
            }

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Set discount {Amount} on item {ItemId} of treatment plan {PlanId}",
                request.DiscountAmount, request.ItemId, plan.Id);

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
            _logger.LogError(ex, "Error setting discount on treatment plan {PlanId}", request.PlanId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de l'enregistrement de la remise.");
        }
    }
}
