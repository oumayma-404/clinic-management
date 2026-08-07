using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users.Commands;

/// <summary>
/// Admin-only: deactivates or reactivates a clinic user (AC-5.3). A deactivated user can no
/// longer log in, but their historical records are retained (no data is deleted).
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetUserActiveCommandHandler> _logger;

    public SetUserActiveCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        ILogger<SetUserActiveCommandHandler> logger)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
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
}
