using MediatR;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

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
    private readonly ISessionFamilyRepository _sessionFamilies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notifications;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IUserRepository userRepository,
        ILocalAuthService localAuthService,
        ISessionFamilyRepository sessionFamilies,
        IUnitOfWork unitOfWork,
        INotificationGenerator notifications,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _userRepository = userRepository;
        _localAuthService = localAuthService;
        _sessionFamilies = sessionFamilies;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _logger = logger;
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

            // ── Replay detection (FR-1.6) ──────────────────────────────────────────────────────────────────
            //
            // The credential is a stateless JWT, so before families nothing could notice one being presented
            // twice. A family records which credential is current; presenting an OLDER one is evidence the
            // chain forked — either the user's copy or a thief's — and that device's session is ended.
            var family = principal.SessionFamilyId is { } familyId
                ? await _sessionFamilies.GetByIdAsync(familyId, cancellationToken)
                : null;

            if (family is not null)
            {
                if (family.UserId != user.Id)
                {
                    // A family belonging to somebody else: the token was forged or transplanted.
                    return Result<LoginResultDto>.Failure(InvalidSessionError);
                }

                var match = family.Match(SessionCredential.Hash(request.RefreshToken));

                if (match == SessionCredentialMatch.None)
                {
                    // ⚠️ ONE device's session ends, never the account. Revoking globally would hand anyone
                    // holding a single stale credential the ability to sign a whole practice out at will,
                    // mid-consultation — `User.TokenVersion` stays untouched here deliberately.
                    family.End("Un identifiant de session déjà remplacé a été présenté.");
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await NotifyReplayAsync(user, family, cancellationToken);
                    return Result<LoginResultDto>.Failure(InvalidSessionError);
                }
            }

            // A pending forced password change is NOT a refusal: the change-password screen itself needs a
            // working access token to submit. The enforcement middleware already restricts such a token to
            // that one endpoint, so surfacing the flag is enough.
            var accessToken = _localAuthService.GenerateToken(user);

            // The durable credential is re-minted too, and the BFF re-sets its cookie with it. Returning only an
            // access token left the cookie holding the token issued at login, so the session died 12 h after
            // sign-in whatever the user was doing — a password prompt mid-afternoon, every afternoon.
            var refreshToken = _localAuthService.GenerateRefreshToken(user, family?.Id);

            if (family is not null)
            {
                // previous ← current, current ← the new one. Run on EVERY successful exchange, including one
                // that presented the predecessor: the racing tab is a legitimate user and gets a working
                // credential of its own.
                family.Rotate(SessionCredential.Hash(refreshToken.AccessToken), refreshToken.ExpiresAtUtc);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

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

    /// <summary>
    /// Tells the user their device's session was ended. Best-effort and post-commit, like every other side
    /// effect here: the family is already closed, and a failed notification must not undo that.
    /// </summary>
    private async Task NotifyReplayAsync(User user, SessionFamily family, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.SessionEndedForReplayAsync(
                user.ClinicId, user.Id, family.DeviceLabel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible de signaler la fin d'une session pour rejeu.");
        }
    }
}
