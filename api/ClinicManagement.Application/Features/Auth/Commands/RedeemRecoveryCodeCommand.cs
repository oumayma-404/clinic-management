using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>
/// Signs in with a single-use recovery code instead of the authenticator
/// (<c>hosted-security-hardening</c> FR-1.4) — way back #1 of three, and the only one the user can take alone.
///
/// <para>⚠️ <b>The password is verified FIRST, so a wrong password burns no code.</b> Otherwise anyone who
/// learned an address could spend all eight by submitting guesses, and the account's own way back would be gone
/// before its owner ever needed it.</para>
///
/// <para>⚠️ <b>The code is spent by its own <c>SaveChangesAsync</c>, BEFORE the active check.</b> A code that has
/// been transmitted has been spent — treating it as unspent because the sign-in then failed for an unrelated
/// reason would make a single-use credential replayable. <c>RedeemPlatformRecoveryCodeCommand</c> is the model,
/// and its two tests pin both halves.</para>
/// </summary>
public class RedeemRecoveryCodeCommand : IRequest<Result<LoginResultDto>>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string RecoveryCode { get; set; } = string.Empty;
}

public class RedeemRecoveryCodeCommandHandler
    : IRequestHandler<RedeemRecoveryCodeCommand, Result<LoginResultDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly ILocalAuthService _localAuthService;
    private readonly ILoginAttemptTracker _attemptTracker;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISessionFamilyRepository _sessionFamilies;

    public RedeemRecoveryCodeCommandHandler(
        IUserRepository userRepository,
        ILocalAuthService localAuthService,
        ILoginAttemptTracker attemptTracker,
        IUnitOfWork unitOfWork,
        ISessionFamilyRepository sessionFamilies)
    {
        _userRepository = userRepository;
        _localAuthService = localAuthService;
        _attemptTracker = attemptTracker;
        _unitOfWork = unitOfWork;
        _sessionFamilies = sessionFamilies;
    }

    private static Result<LoginResultDto> Refuse(string code) =>
        Result<LoginResultDto>.Failure(ClinicAuthRefusals.MessageFor(code)!, code);

    public async Task<Result<LoginResultDto>> Handle(
        RedeemRecoveryCodeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email)
                || string.IsNullOrWhiteSpace(request.Password)
                || string.IsNullOrWhiteSpace(request.RecoveryCode))
            {
                return Refuse(ClinicAuthRefusals.InvalidCredentials);
            }

            var user = await _userRepository.GetByEmailAsync(request.Email.Trim(), cancellationToken);
            if (user is null || !user.IsLocalAccount())
            {
                return Refuse(ClinicAuthRefusals.InvalidCredentials);
            }

            // Both lockout tiers, before the password — the same brake the ordinary ladder applies, or this
            // endpoint would be the unrated door beside a rate-limited one.
            if (_attemptTracker.IsLockedOutForCurrentSource(user.Id) || user.IsLockedOut())
            {
                return Result<LoginResultDto>.Failure(
                    "Ce compte est temporairement bloqué après plusieurs tentatives de connexion échouées. Veuillez réessayer plus tard.",
                    ClinicAuthRefusals.TooManyAttempts);
            }

            // ⚠️ First, so a wrong password spends nothing.
            if (_localAuthService.VerifyPassword(user.PasswordHash!, request.Password)
                == PasswordVerificationOutcome.Failed)
            {
                _attemptTracker.RecordFailure(user.Id);
                user.RecordFailedLogin();
                _userRepository.Update(user);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Refuse(ClinicAuthRefusals.InvalidCredentials);
            }

            if (!user.IsTotpEnrolled)
            {
                return Refuse(ClinicAuthRefusals.TotpNotEnrolled);
            }

            var spent = user.ConsumeRecoveryCode(request.RecoveryCode);

            // ⚠️ Persisted here, on its own save, whatever happens next. Everything below can still refuse the
            // sign-in, and the code must stay spent through every one of those exits.
            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (!spent)
            {
                _attemptTracker.RecordFailure(user.Id);
                return Refuse(ClinicAuthRefusals.TotpInvalid);
            }

            // Disclosed only to a caller who already proved the password and a code — i.e. the owner.
            if (!user.IsActive)
            {
                return Result<LoginResultDto>.Failure(
                    user.IsPendingActivation
                        ? "Votre compte a bien été créé mais doit encore être activé par un administrateur de la clinique. Vous pourrez vous connecter dès qu'il l'aura fait."
                        : "Ce compte a été désactivé. Veuillez contacter l'administrateur de votre clinique.",
                    ClinicAuthRefusals.AccountDisabled);
            }

            _attemptTracker.ClearForCurrentSource(user.Id);
            user.RecordSuccessfulLogin();
            var token = _localAuthService.GenerateToken(user);

            // A sign-in, so it opens a chain exactly as the ordinary ladder does (FR-1.6) — staged before the
            // single save, for the reason `LoginCommand` states.
            var family = new SessionFamily(
                user.Id, SessionCredential.Hash(Guid.NewGuid().ToString()), DateTime.UtcNow);
            await _sessionFamilies.AddAsync(family, cancellationToken);

            var refreshToken = _localAuthService.GenerateRefreshToken(user, family.Id);
            family.Rotate(SessionCredential.Hash(refreshToken.AccessToken), refreshToken.ExpiresAtUtc);

            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<LoginResultDto>.Success(new LoginResultDto
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
            });
        }
        catch (Exception)
        {
            return Result<LoginResultDto>.Failure(
                "Une erreur inattendue est survenue lors de la connexion. Veuillez réessayer.");
        }
    }
}
