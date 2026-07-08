using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// Lets the authenticated (Local-mode) user set a new password after verifying the current
/// one. Clears the forced-change flag (AC-5.2 next-login change). Any role may change their
/// own password.
/// </summary>
public class ChangePasswordCommand : IRequest<Result>
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly ILocalAuthService _localAuthService;
    private readonly IUnitOfWork _unitOfWork;

    public ChangePasswordCommandHandler(
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

    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result.Failure("User ID not found in token");
            }

            var user = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (user == null || !user.IsLocalAccount())
            {
                return Result.Failure("User not found");
            }

            if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length < PasswordPolicy.MinLength)
            {
                return Result.Failure($"Password must be at least {PasswordPolicy.MinLength} characters.");
            }

            var outcome = _localAuthService.VerifyPassword(user.PasswordHash!, request.CurrentPassword);
            if (outcome == PasswordVerificationOutcome.Failed)
            {
                return Result.Failure("The current password is incorrect.");
            }

            var newHash = _localAuthService.HashPassword(request.NewPassword);
            user.SetPassword(newHash, mustChangePassword: false);

            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception)
        {
            // Authenticated endpoint, but still avoid echoing internal exception details to the caller.
            return Result.Failure("An unexpected error occurred while changing the password. Please try again.");
        }
    }
}
