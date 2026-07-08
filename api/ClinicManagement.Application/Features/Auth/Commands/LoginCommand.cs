using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// Local-mode login: authenticates an email + password against a local account
/// and returns an app-signed JWT. No-op / not used in Cloud mode.
/// </summary>
public class LoginCommand : IRequest<Result<LoginResultDto>>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResultDto>>
{
    // Deliberately generic so we never reveal whether the email exists.
    private const string InvalidCredentialsError = "Invalid email or password.";

    private readonly IUserRepository _userRepository;
    private readonly ILocalAuthService _localAuthService;
    private readonly IUnitOfWork _unitOfWork;

    public LoginCommandHandler(
        IUserRepository userRepository,
        ILocalAuthService localAuthService,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _localAuthService = localAuthService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LoginResultDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Result<LoginResultDto>.Failure(InvalidCredentialsError);
            }

            var user = await _userRepository.GetByEmailAsync(request.Email.Trim(), cancellationToken);

            // No such account, or a Cloud (Auth0) account with no local password.
            if (user == null || !user.IsLocalAccount())
            {
                return Result<LoginResultDto>.Failure(InvalidCredentialsError);
            }

            if (!user.IsActive)
            {
                return Result<LoginResultDto>.Failure("This account has been deactivated. Please contact your clinic administrator.");
            }

            if (user.IsLockedOut())
            {
                return Result<LoginResultDto>.Failure("This account is temporarily locked due to repeated failed logins. Please try again later.");
            }

            var outcome = _localAuthService.VerifyPassword(user.PasswordHash!, request.Password);
            if (outcome == PasswordVerificationOutcome.Failed)
            {
                user.RecordFailedLogin();
                _userRepository.Update(user);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<LoginResultDto>.Failure(InvalidCredentialsError);
            }

            user.RecordSuccessfulLogin();
            var token = _localAuthService.GenerateToken(user);

            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var result = new LoginResultDto
            {
                AccessToken = token.AccessToken,
                ExpiresAt = token.ExpiresAtUtc,
                MustChangePassword = user.MustChangePassword,
                User = new UserDto
                {
                    Id = user.Id,
                    ClinicId = user.ClinicId,
                    Role = user.Role,
                    Email = user.Email,
                    FullName = user.FullName,
                    CreatedAt = user.CreatedAt
                }
            };

            return Result<LoginResultDto>.Success(result);
        }
        catch (Exception)
        {
            // Anonymous endpoint: do not echo internal exception details to the caller.
            return Result<LoginResultDto>.Failure("An unexpected error occurred during login. Please try again.");
        }
    }
}
