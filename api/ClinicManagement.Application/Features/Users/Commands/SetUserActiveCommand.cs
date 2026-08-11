using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users.Commands;

/// <summary>
/// Admin-only: deactivates or reactivates a clinic user (AC-5.3). A deactivated user can no
/// longer log in, but their historical records are retained (no data is deleted).
///
/// <para><b>⚠️ The endpoint's <c>[AllowsWithoutSubscription]</c> is one-directional and this handler is what makes
/// it so.</b> Its recorded reason — « offboarding must not wait on an invoice » — justifies the *deactivation*
/// half only, but the action carries both, so an expired cabinet could bring any switched-off account back online:
/// the same effect as creating one, which the gate correctly refuses on <c>POST /api/users</c>. Reactivation is
/// therefore refused here, with the gate's own sentence, so the exempt surface matches the reason on it.</para>
/// </summary>
public class SetUserActiveCommand : IRequest<Result<ClinicUserDto>>
{
    public string TargetUserId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class SetUserActiveCommandHandler : IRequestHandler<SetUserActiveCommand, Result<ClinicUserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly ISubscriptionPolicy _subscriptionPolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetUserActiveCommandHandler> _logger;

    public SetUserActiveCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IClinicSubscriptionRepository subscriptions,
        ISubscriptionPolicy subscriptionPolicy,
        IUnitOfWork unitOfWork,
        ILogger<SetUserActiveCommandHandler> logger)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _subscriptions = subscriptions;
        _subscriptionPolicy = subscriptionPolicy;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ClinicUserDto>> Handle(SetUserActiveCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ClinicUserDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<ClinicUserDto>.Failure("Utilisateur introuvable.");
            }

            // AC-5.4: only an admin can (de)activate users.
            if (!admin.IsAdmin())
            {
                return Result<ClinicUserDto>.Failure("Seuls les administrateurs peuvent modifier le statut d'un utilisateur.");
            }

            if (string.IsNullOrWhiteSpace(request.TargetUserId))
            {
                return Result<ClinicUserDto>.Failure("L'utilisateur cible est requis.");
            }

            // An admin deactivating themselves would be an unrecoverable lockout in Phase 1
            // (the recovery utility resets a password, not the active flag), so block it.
            if (!request.IsActive && string.Equals(request.TargetUserId, admin.Id, StringComparison.Ordinal))
            {
                return Result<ClinicUserDto>.Failure("Vous ne pouvez pas désactiver votre propre compte.");
            }

            var target = await _userRepository.GetByIdAsync(request.TargetUserId, cancellationToken);
            // Scope to the admin's own clinic.
            if (target == null || target.ClinicId != admin.ClinicId)
            {
                return Result<ClinicUserDto>.Failure("Utilisateur introuvable.");
            }

            if (request.IsActive)
            {
                var refusal = await RefuseReactivationAsync(admin.ClinicId, cancellationToken);
                if (refusal is not null)
                {
                    return refusal;
                }

                target.Activate();
            }
            else
            {
                target.Deactivate();
            }

            _userRepository.Update(target);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ClinicUserDto>.Success(new ClinicUserDto
            {
                Id = target.Id,
                ClinicId = target.ClinicId,
                Role = target.Role,
                Email = target.Email,
                FullName = target.FullName,
                IsActive = target.IsActive,
                MustChangePassword = target.MustChangePassword,
                LastLoginAt = target.LastLoginAt,
                CreatedAt = target.CreatedAt
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // Same A-8 defect class as DeleteMedicalDocumentCommand: English text plus the raw exception,
            // straight to a French-speaking clinic. Fixed here because step 7 builds its sibling command on
            // this handler's guards, so leaving one of the pair leaking would be the drift the sweep exists to
            // prevent.
            _logger.LogError(ex, "Unhandled failure updating the status of user {TargetUserId}", request.TargetUserId);
            return Result<ClinicUserDto>.Failure("Erreur lors de la modification du statut de l'utilisateur. Veuillez réessayer.");
        }
    }

    /// <summary>
    /// The subscription half of the endpoint's exemption, applied to the reactivation direction only. Returns the
    /// gate's own refusal — same sentence, same code — or null where the cabinet may write.
    /// </summary>
    private async Task<Result<ClinicUserDto>?> RefuseReactivationAsync(
        Guid clinicId, CancellationToken cancellationToken)
    {
        if (!_subscriptionPolicy.RequiresSubscription)
        {
            return null;
        }

        var subscription = await _subscriptions.GetByClinicAsync(clinicId, cancellationToken);
        if (subscription is null)
        {
            return Result<ClinicUserDto>.Failure(SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode);
        }

        var status = SubscriptionStateReader.Read(subscription, ClinicClock.ClinicToday());
        if (status.AllowsWrites)
        {
            return null;
        }

        return status switch
        {
            { State: SubscriptionState.Suspended } =>
                Result<ClinicUserDto>.Failure(SubscriptionRefusals.Suspended, SubscriptionRefusals.SuspendedCode),
            { EndsOn: { } endsOn } =>
                Result<ClinicUserDto>.Failure(
                    SubscriptionRefusals.Required(endsOn), SubscriptionRefusals.RequiredCode),
            _ => Result<ClinicUserDto>.Failure(SubscriptionRefusals.Missing, SubscriptionRefusals.MissingCode),
        };
    }
}
