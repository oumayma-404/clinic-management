using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Void a payment recorded against one of a devis's échéances — "this was never received". The ledger row is
/// kept and marked; the installment's stored totals are re-derived from the remaining live rows.
///
/// <para>
/// The plan's <b>status is deliberately not walked back</b>, unlike an invoice's: a plan's status tracks
/// clinical progress (« Terminé » means every act is done, not that it is paid), so correcting a payment must
/// not un-start or un-complete the treatment.
/// </para>
/// </summary>
public class VoidInstallmentPaymentCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }
    public Guid InstallmentId { get; set; }
    public Guid PaymentId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class VoidInstallmentPaymentCommandHandler
    : IRequestHandler<VoidInstallmentPaymentCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<VoidInstallmentPaymentCommandHandler> _logger;

    public VoidInstallmentPaymentCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<VoidInstallmentPaymentCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        VoidInstallmentPaymentCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<TreatmentPlanDto>.Failure("Le motif d'annulation du paiement est requis.");
            }

            // Tenant isolation: a plan from another clinic reads as "not found".
            var plan = await _planRepository.GetByIdAsync(request.PlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            var actorUserId = _clinicContext.GetUserId();
            var actorName = await ResolveActorNameAsync(actorUserId, cancellationToken);

            plan.VoidInstallmentPayment(
                request.InstallmentId, request.PaymentId, request.Reason, actorUserId, actorName);

            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Voided installment payment {PaymentId} on plan {PlanId}; collected is now {Collected}",
                request.PaymentId, plan.Id, plan.AmountPaid);

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
        catch (Exception)
        {
            return Result<TreatmentPlanDto>.Failure("Erreur lors de l'annulation du paiement.");
        }
    }

    /// <summary>Best-effort name snapshot for the trail — a missing user must never block the correction.</summary>
    private async Task<string?> ResolveActorNameAsync(string? actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return null;
        }

        var user = await _userRepository.GetByAuth0SubAsync(actorUserId, cancellationToken);
        return user?.FullName ?? user?.Email;
    }
}
