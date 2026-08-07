using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users.Commands;

/// <summary>
/// Admin-only: resets a clinic user's password to a fresh temporary one and forces a change
/// at next login (AC-5.2). The temporary password is returned once for the admin to relay.
/// </summary>
public class ResetUserPasswordCommand : IRequest<Result<ResetPasswordResultDto>>
{
    public string TargetUserId { get; set; } = string.Empty;
}

public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand, Result<ResetPasswordResultDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly ILocalAuthService _localAuthService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ResetUserPasswordCommandHandler> _logger;

    public ResetUserPasswordCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        ILocalAuthService localAuthService,
        IUnitOfWork unitOfWork,
        ILogger<ResetUserPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _localAuthService = localAuthService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<ResetPasswordResultDto>> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ResetPasswordResultDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<ResetPasswordResultDto>.Failure("Utilisateur introuvable.");
            }

            // AC-5.4: only an admin can reset another user's password.
            if (!admin.IsAdmin())
            {
                return Result<ResetPasswordResultDto>.Failure("Seuls les administrateurs peuvent réinitialiser les mots de passe.");
            }

            if (string.IsNullOrWhiteSpace(request.TargetUserId))
            {
                return Result<ResetPasswordResultDto>.Failure("L'utilisateur cible est requis.");
            }

            var target = await _userRepository.GetByIdAsync(request.TargetUserId, cancellationToken);
            // Scope to the admin's own clinic — never expose or mutate users of another clinic.
            if (target == null || target.ClinicId != admin.ClinicId)
            {
                return Result<ResetPasswordResultDto>.Failure("Utilisateur introuvable.");
            }

            // Only local (password-backed) accounts have a password to reset.
            if (!target.IsLocalAccount())
            {
                return Result<ResetPasswordResultDto>.Failure("Ce compte n'utilise pas de mot de passe local.");
            }

            var temporaryPassword = _localAuthService.GenerateTemporaryPassword();
            var passwordHash = _localAuthService.HashPassword(temporaryPassword);
            target.SetPassword(passwordHash, mustChangePassword: true);

            _userRepository.Update(target);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<ResetPasswordResultDto>.Success(new ResetPasswordResultDto
            {
                UserId = target.Id,
                TemporaryPassword = temporaryPassword
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // A-8 defect class (see SetUserActiveCommand): English + the raw exception. A password-reset failure
            // is also the last place to echo server internals back to a caller.
            _logger.LogError(ex, "Unhandled failure resetting the password of user {TargetUserId}", request.TargetUserId);
            return Result<ResetPasswordResultDto>.Failure("Erreur lors de la réinitialisation du mot de passe. Veuillez réessayer.");
        }
    }
}
