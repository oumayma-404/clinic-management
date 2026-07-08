using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
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

    public ResetUserPasswordCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        ILocalAuthService localAuthService,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _localAuthService = localAuthService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ResetPasswordResultDto>> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<ResetPasswordResultDto>.Failure("User ID not found in token");
            }

            var admin = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (admin == null)
            {
                return Result<ResetPasswordResultDto>.Failure("User not found");
            }

            // AC-5.4: only an admin can reset another user's password.
            if (!admin.IsAdmin())
            {
                return Result<ResetPasswordResultDto>.Failure("Only admins can reset passwords");
            }

            if (string.IsNullOrWhiteSpace(request.TargetUserId))
            {
                return Result<ResetPasswordResultDto>.Failure("Target user is required.");
            }

            var target = await _userRepository.GetByIdAsync(request.TargetUserId, cancellationToken);
            // Scope to the admin's own clinic — never expose or mutate users of another clinic.
            if (target == null || target.ClinicId != admin.ClinicId)
            {
                return Result<ResetPasswordResultDto>.Failure("User not found");
            }

            // Only local (password-backed) accounts have a password to reset.
            if (!target.IsLocalAccount())
            {
                return Result<ResetPasswordResultDto>.Failure("This account does not use a local password.");
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
        catch (Exception ex)
        {
            return Result<ResetPasswordResultDto>.Failure($"Error resetting password: {ex.Message}");
        }
    }
}
