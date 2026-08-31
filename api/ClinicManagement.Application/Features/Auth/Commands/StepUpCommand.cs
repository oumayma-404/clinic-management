using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>What a successful step-up hands back: a single-use token for one named action.</summary>
public record StepUpDto(string ConfirmationToken);

/// <summary>
/// Re-authenticates the signed-in user for one sensitive action
/// (<c>hosted-security-hardening</c> FR-1.8; applied to the archive export by FR-4.3).
///
/// <para>⚠️ <b>It accepts the password OR a current TOTP code</b> (OQ-2), and the alternative is what keeps AC-7
/// true: a user who signs into the shell by biometrics may genuinely not remember their password, and demanding
/// it would make the guarded action unreachable for them. Either proves presence, which is what a step-up is
/// for.</para>
///
/// <para>⚠️ <b>It spends its OWN failure counter and never the login lockout.</b> Three wrong attempts refuse
/// this action with the session <b>untouched</b> — the user stays signed in and keeps working. Wiring it to the
/// login lockout would turn a mistyped confirmation into « ce compte est bloqué » for somebody who was already
/// authenticated, which is a self-inflicted outage.</para>
/// </summary>
public class StepUpCommand : IRequest<Result<StepUpDto>>
{
    /// <summary>What the confirmation will authorise. A token minted for one action does not open another.</summary>
    public string Action { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string? TotpCode { get; set; }
}

public class StepUpCommandHandler : IRequestHandler<StepUpCommand, Result<StepUpDto>>
{
    private const string TooManyAttempts =
        "Trop de tentatives. Réessayez dans quelques minutes — votre session reste ouverte.";

    private const string WrongProof =
        "Mot de passe ou code de vérification incorrect.";

    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly ILocalAuthService _localAuthService;
    private readonly ITotpService _totpService;
    private readonly IUserSecretProtector _secretProtector;
    private readonly IStepUpConfirmations _confirmations;
    private readonly ITotpReplayGuard _totpReplayGuard;

    public StepUpCommandHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        ILocalAuthService localAuthService,
        ITotpService totpService,
        IUserSecretProtector secretProtector,
        IStepUpConfirmations confirmations,
        ITotpReplayGuard totpReplayGuard)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _localAuthService = localAuthService;
        _totpService = totpService;
        _secretProtector = secretProtector;
        _confirmations = confirmations;
        _totpReplayGuard = totpReplayGuard;
    }

    public async Task<Result<StepUpDto>> Handle(StepUpCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Action))
            {
                return Result<StepUpDto>.Failure("Action non précisée.");
            }

            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result<StepUpDto>.Failure(ErrorMessages.Generic);
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user is null)
            {
                return Result<StepUpDto>.Failure(ErrorMessages.Generic);
            }

            // Either proof will do. Checked in this order because a password is the one every account has.
            var proved = false;

            if (!string.IsNullOrWhiteSpace(request.Password) && user.IsLocalAccount())
            {
                proved = _localAuthService.VerifyPassword(user.PasswordHash!, request.Password)
                         != PasswordVerificationOutcome.Failed;
            }

            if (!proved && !string.IsNullOrWhiteSpace(request.TotpCode) && user.IsTotpEnrolled)
            {
                // An undecryptable secret proves nothing; it never counts as a pass.
                //
                // ⚠️ Verified FIRST, then claimed — same order as `LoginCommand`, so a wrong guess cannot burn
                // the real code's one use. This is the third of the three sites that verify a TOTP code, and
                // until now it was one of the two that did not consume it: step-up is what gates the archive
                // export and user management, so a code captured at the login screen was re-presentable here
                // inside its window to authorise exactly the operations step-up exists to protect.
                proved = _secretProtector.TryUnprotect(user.ProtectedTotpSecret!, out var secret)
                         && _totpService.VerifyCode(secret, request.TotpCode!)
                         && _totpReplayGuard.TryConsume(user.Id, request.TotpCode!);
            }

            if (!proved)
            {
                var exhausted = _confirmations.RecordFailureAndCheckExhausted(user.Id);
                // Note what does NOT happen here: no RecordFailedLogin, no attempt tracker, no TokenVersion.
                // The session is deliberately untouched.
                return Result<StepUpDto>.Failure(exhausted ? TooManyAttempts : WrongProof);
            }

            _confirmations.ClearFailures(user.Id);
            return Result<StepUpDto>.Success(
                new StepUpDto(_confirmations.Issue(user.Id, request.Action.Trim())));
        }
        catch (Exception)
        {
            return Result<StepUpDto>.Failure(ErrorMessages.Generic);
        }
    }
}
