using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// Exchanges the durable session credential (the refresh token held in the BFF's HttpOnly cookie) for a
/// fresh short-lived access token — security-hardening US-5.
///
/// This is what lets the browser hold a credential valid for only ~30 minutes without the user ever being
/// bounced to the login screen (AC-5.3 / AC-5.4).
/// </summary>
public class RefreshTokenCommand : IRequest<Result<LoginResultDto>>
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<LoginResultDto>>
{
    // One message for every rejection: the caller learns only that it must sign in again, never why. An
    // expired token, a revoked one and a forged one must be indistinguishable.
    private const string InvalidSessionError = "Votre session n'est plus valide. Veuillez vous reconnecter.";

    private readonly IUserRepository _userRepository;
    private readonly ILocalAuthService _localAuthService;

    public RefreshTokenCommandHandler(IUserRepository userRepository, ILocalAuthService localAuthService)
    {
        _userRepository = userRepository;
        _localAuthService = localAuthService;
    }

    public async Task<Result<LoginResultDto>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Signature, issuer, refresh audience and lifetime. An ACCESS token presented here fails the
            // audience check, so the two kinds cannot be swapped in either direction.
            var principal = _localAuthService.ValidateRefreshToken(request.RefreshToken);
            if (principal is null)
            {
                return Result<LoginResultDto>.Failure(InvalidSessionError);
            }

            var user = await _userRepository.GetByAuth0SubAsync(principal.Subject, cancellationToken);
            if (user is null || !user.IsLocalAccount())
            {
                return Result<LoginResultDto>.Failure(InvalidSessionError);
            }

            // AC-5.6: renewal re-checks LIVE account state, so a session revoked since the cookie was issued
            // cannot mint itself a new access token. Without this the refresh token would be the very
            // long-lived, unrevocable credential this feature exists to remove.
            if (principal.TokenVersion != user.TokenVersion)
            {
                return Result<LoginResultDto>.Failure(InvalidSessionError);
            }

            if (!user.IsActive)
            {
                return Result<LoginResultDto>.Failure(InvalidSessionError);
            }

            // A pending forced password change is NOT a refusal: the change-password screen itself needs a
            // working access token to submit. The enforcement middleware already restricts such a token to
            // that one endpoint, so surfacing the flag is enough.
            var accessToken = _localAuthService.GenerateToken(user);

            return Result<LoginResultDto>.Success(new LoginResultDto
            {
                AccessToken = accessToken.AccessToken,
                ExpiresAt = accessToken.ExpiresAtUtc,
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
            });
        }
        catch (Exception)
        {
            // Anonymous endpoint: never echo internal detail.
            return Result<LoginResultDto>.Failure(InvalidSessionError);
        }
    }
}
