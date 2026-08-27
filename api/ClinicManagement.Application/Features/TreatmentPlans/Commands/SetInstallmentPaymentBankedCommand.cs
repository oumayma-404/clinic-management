using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// The échéancier's half of « marquer un chèque encaissé » — see
/// <see cref="Invoices.Commands.SetPaymentBankedCommand"/> for why banking moves no figure and why it is
/// reversible.
///
/// <para>
/// ⚠️ It takes <b>three</b> ids because an <c>InstallmentPayment</c> is only addressable as
/// {plan, installment, payment} — exactly the shape <see cref="VoidInstallmentPaymentCommand"/> already has. A
/// payment-id-only route would need two repository lookups that exist for no other reason.
/// </para>
/// <para>
/// ⚠️ A devis whose cheques were carried onto a <b>bridge invoice</b> is excluded from « chèques à encaisser »
/// wholesale, so once a plan is billed only the invoice-side row is reachable and one physical cheque cannot be
/// marked on both tracks (B-1). The carry is one-way and one-time, at issue.
/// </para>
/// </summary>
public class SetInstallmentPaymentBankedCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }
    public Guid InstallmentId { get; set; }
    public Guid PaymentId { get; set; }

    /// <summary>True to record it as banked, false to clear the mark.</summary>
    public bool Banked { get; set; }
}

public class SetInstallmentPaymentBankedCommandHandler
    : IRequestHandler<SetInstallmentPaymentBankedCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetInstallmentPaymentBankedCommandHandler> _logger;

    public SetInstallmentPaymentBankedCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<SetInstallmentPaymentBankedCommandHandler> logger)
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
        SetInstallmentPaymentBankedCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            // Tenant isolation: a plan from another clinic reads as "not found".
            var plan = await _planRepository.GetByIdAsync(request.PlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            var actorUserId = _clinicContext.GetUserId();
            var actorName = await ResolveActorNameAsync(actorUserId, cancellationToken);

            plan.SetInstallmentPaymentBanked(
                request.InstallmentId, request.PaymentId, request.Banked, actorUserId, actorName);

            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Cheque installment payment {PaymentId} on plan {PlanId} marked banked={Banked}",
                request.PaymentId, plan.Id, request.Banked);

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
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la mise à jour de l'encaissement du chèque.");
        }
    }

    /// <summary>Best-effort name snapshot for the trail — a missing user must never block the mark.</summary>
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
