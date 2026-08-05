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
///
/// <para><b>Sliding expiry, not revoking rotation</b> (mobile-native-shells AC-35 / AC-39). Every exchange
/// mints a <i>new</i> refresh credential, so a staff member who keeps working keeps their session — but the
/// superseded one stays valid until its own expiry, because it is a stateless JWT and nothing stores it. That
/// is deliberate: two tabs exchanging at once must both keep working, and <c>TokenVersion</c> remains the only
/// revocation (LEARNINGS: server-side state changes don't take effect until expiry). A test asserting that a
/// superseded credential is refused would pin a property this design does not claim.</para>
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

            // The durable credential is re-minted too, and the BFF re-sets its cookie with it. Returning only an
            // access token left the cookie holding the token issued at login, so the session died 12 h after
            // sign-in whatever the user was doing — a password prompt mid-afternoon, every afternoon.
            var refreshToken = _localAuthService.GenerateRefreshToken(user);

            return Result<LoginResultDto>.Success(new LoginResultDto
            {
                AccessToken = accessToken.AccessToken,
                ExpiresAt = accessToken.ExpiresAtUtc,
                RefreshToken = refreshToken.AccessToken,
                RefreshExpiresAt = refreshToken.ExpiresAtUtc,
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
