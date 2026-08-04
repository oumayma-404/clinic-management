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

    // Same wording for both lockout tiers: the caller must not learn which brake stopped them.
    private const string LockedOutError =
        "Ce compte est temporairement bloqué après plusieurs tentatives de connexion échouées. Veuillez réessayer plus tard.";

    private readonly IUserRepository _userRepository;
    private readonly ILocalAuthService _localAuthService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILoginAttemptTracker _attemptTracker;

    public LoginCommandHandler(
        IUserRepository userRepository,
        ILocalAuthService localAuthService,
        IUnitOfWork unitOfWork,
        ILoginAttemptTracker attemptTracker)
    {
        _userRepository = userRepository;
        _localAuthService = localAuthService;
        _unitOfWork = unitOfWork;
        _attemptTracker = attemptTracker;
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

            // Both lockouts are checked before the password so a brute-force attempt is actually
            // stopped (AC-3.4) — this necessarily discloses the locked state, an accepted
            // trade-off. The deactivated state, by contrast, is disclosed only after a correct
            // password (below) so it can't be used to enumerate accounts.
            //
            // Primary brake: this source has burned its attempts against this account (AC-4.2). Only the
            // offending machine is refused — a colleague on another PC signs in normally, which is the whole
            // point: the previous account-only lockout let one hostile host lock the entire clinic out.
            if (_attemptTracker.IsLockedOutForCurrentSource(user.Id))
            {
                return Result<LoginResultDto>.Failure(LockedOutError);
            }

            // Durable cross-source backstop (AC-4.3), at a threshold no single source can reach alone. Also
            // what survives the restart that clears the in-memory per-source counters.
            if (user.IsLockedOut())
            {
                return Result<LoginResultDto>.Failure(LockedOutError);
            }

            var outcome = _localAuthService.VerifyPassword(user.PasswordHash!, request.Password);
            if (outcome == PasswordVerificationOutcome.Failed)
            {
                _attemptTracker.RecordFailure(user.Id);
                user.RecordFailedLogin();
                _userRepository.Update(user);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result<LoginResultDto>.Failure(InvalidCredentialsError);
            }

            // Disclosed only to a caller who supplied the correct password (the account owner).
            //
            // The two inactive states read differently to the person in front of the screen, and telling them
            // apart is the whole point of I5's pending state: someone who registered ten seconds ago has done
            // nothing wrong and needs to know an approval is coming, while « désactivé » on a freshly-created
            // account reads as a bug in the registration they just completed. Both messages point at the same
            // person; only one of them is an accusation.
            if (!user.IsActive)
            {
                return Result<LoginResultDto>.Failure(user.IsPendingActivation
                    ? "Votre compte a bien été créé mais doit encore être activé par un administrateur de la clinique. Vous pourrez vous connecter dès qu'il l'aura fait."
                    : "Ce compte a été désactivé. Veuillez contacter l'administrateur de votre clinique.");
            }

            // The stored hash used an outdated format — upgrade it now that we have the plaintext.
            if (outcome == PasswordVerificationOutcome.SuccessNeedsRehash)
            {
                user.UpgradePasswordHash(_localAuthService.HashPassword(request.Password));
            }

            // A user who simply mistyped should not carry a penalty into their next session.
            _attemptTracker.ClearForCurrentSource(user.Id);
            user.RecordSuccessfulLogin();
            var token = _localAuthService.GenerateToken(user);

            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // The durable session credential the BFF stores in its HttpOnly cookie; the access token above is
            // held only in browser memory and renewed from this (US-5).
            var refreshToken = _localAuthService.GenerateRefreshToken(user);

            var result = new LoginResultDto
            {
                AccessToken = token.AccessToken,
                RefreshToken = refreshToken.AccessToken,
                ExpiresAt = token.ExpiresAtUtc,
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
            };

            return Result<LoginResultDto>.Success(result);
        }
        catch (Exception)
        {
            // Anonymous endpoint: do not echo internal exception details to the caller.
            return Result<LoginResultDto>.Failure("Une erreur inattendue est survenue lors de la connexion. Veuillez réessayer.");
        }
    }
}
