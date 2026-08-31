using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>The regenerated codes, shown once.</summary>
public record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

/// <summary>
/// Replaces every recovery code with a fresh set (<c>hosted-security-hardening</c> FR-1.5).
///
/// <para>Requires a <b>current code</b>: the whole value of regeneration is that it invalidates codes that may
/// have been photographed or left on a desk, so allowing it on the session alone would let whoever is holding an
/// unlocked machine mint themselves eight new ones.</para>
///
/// <para>It deliberately does not bump <c>TokenVersion</c> — the authenticator has not changed, and signing
/// somebody out of every device for good hygiene is a reason not to do it again.</para>
/// </summary>
public class RegenerateRecoveryCodesCommand : IRequest<Result<RecoveryCodesDto>>
{
    public string TotpCode { get; set; } = string.Empty;
}

/// <summary>
/// Removes the second factor entirely (<c>hosted-security-hardening</c> FR-1.5).
///
/// <para>⚠️ <b>The administrator refusal is gated on the capability, not on the role alone.</b> An unconditional
/// « un administrateur ne peut pas désactiver » would strand an admin who enrolled <i>voluntarily</i> on
/// `SelfHostedLan` — permanently unable to remove a control their deployment never required. That is a control
/// with no way out, on precisely the profile the capability's own documentation says must not have one.</para>
/// </summary>
public class DisableTotpCommand : IRequest<Result>
{
    public string TotpCode { get; set; } = string.Empty;
}

public class ManageTotpCommandHandler
    : IRequestHandler<RegenerateRecoveryCodesCommand, Result<RecoveryCodesDto>>,
      IRequestHandler<DisableTotpCommand, Result>
{
    private const string WrongCode = "Code de vérification invalide.";
    private const string NotEnrolled = "Le second facteur n'est pas enrôlé pour ce compte.";

    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly ITotpService _totpService;
    private readonly IUserSecretProtector _secretProtector;
    private readonly ISecondFactorPolicy _secondFactorPolicy;
    private readonly ITotpReplayGuard _totpReplayGuard;
    private readonly IUnitOfWork _unitOfWork;

    public ManageTotpCommandHandler(
        IClinicContext clinicContext,
        IUserRepository userRepository,
        ITotpService totpService,
        IUserSecretProtector secretProtector,
        ISecondFactorPolicy secondFactorPolicy,
        ITotpReplayGuard totpReplayGuard,
        IUnitOfWork unitOfWork)
    {
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _totpService = totpService;
        _secretProtector = secretProtector;
        _secondFactorPolicy = secondFactorPolicy;
        _totpReplayGuard = totpReplayGuard;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Loads the caller and checks the presented code against their own factor.</summary>
    private async Task<(User? User, string? Error)> AuthoriseAsync(string totpCode, CancellationToken ct)
    {
        var userId = _clinicContext.GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (null, ErrorMessages.Generic);
        }

        var user = await _userRepository.GetByAuth0SubAsync(userId, ct);
        if (user is null)
        {
            return (null, ErrorMessages.Generic);
        }

        if (!user.IsTotpEnrolled)
        {
            return (null, NotEnrolled);
        }

        // ⚠️ Verified FIRST, then claimed — same order as `LoginCommand`, so a wrong guess cannot burn the real
        // code's one use. What this helper gates is not a sign-in: it is « regenerate my recovery codes » and
        // « disable my second factor », so a code captured once and replayed here hands over the two operations
        // that dismantle the factor itself.
        if (!_secretProtector.TryUnprotect(user.ProtectedTotpSecret!, out var secret)
            || !_totpService.VerifyCode(secret, totpCode)
            || !_totpReplayGuard.TryConsume(user.Id, totpCode))
        {
            return (null, WrongCode);
        }

        return (user, null);
    }

    public async Task<Result<RecoveryCodesDto>> Handle(
        RegenerateRecoveryCodesCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var (user, error) = await AuthoriseAsync(request.TotpCode, cancellationToken);
            if (user is null)
            {
                return Result<RecoveryCodesDto>.Failure(error!);
            }

            var codes = Enumerable
                .Range(0, UserRecoveryCode.CountPerEnrolment)
                .Select(_ => UserRecoveryCode.NewCode())
                .ToList();

            user.ReplaceRecoveryCodes(codes);
            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<RecoveryCodesDto>.Success(new RecoveryCodesDto(codes));
        }
        catch (Exception ex) when (ex is not Common.Exceptions.ConflictException)
        {
            return Result<RecoveryCodesDto>.Failure(ErrorMessages.Generic);
        }
    }

    public async Task<Result> Handle(DisableTotpCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var (user, error) = await AuthoriseAsync(request.TotpCode, cancellationToken);
            if (user is null)
            {
                return Result.Failure(error!);
            }

            // ⚠️ The capability AND the role, never the role alone — see the command's own note.
            if (_secondFactorPolicy.RequiresAdminSecondFactor && user.IsAdmin())
            {
                return Result.Failure(
                    "Le second facteur est obligatoire pour les administrateurs de cette installation et ne peut pas être désactivé.");
            }

            user.DisableTotp();
            _userRepository.Update(user);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception ex) when (ex is not Common.Exceptions.ConflictException)
        {
            return Result.Failure(ErrorMessages.Generic);
        }
    }
}
