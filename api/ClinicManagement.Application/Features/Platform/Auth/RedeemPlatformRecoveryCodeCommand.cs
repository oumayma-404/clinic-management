using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Platform.Auth;

/// <summary>
/// Signs in with a recovery code instead of a one-time code — the path back in when the authenticator is gone
/// (AC-1.3b, EC-3).
///
/// <para>⚠️ <b>The code is consumed whether or not the sign-in completes</b>, and that ordering is the whole
/// requirement. A code that has been transmitted has been exposed, so treating it as unspent because a *later*
/// check refused — a deactivated account, a save failure — would make a single-use credential replayable. The
/// consumption is therefore saved in its own <c>SaveChangesAsync</c> <b>before</b> the remaining checks run.</para>
///
/// <para>⚠️ <b>The password is verified first, and that is not the same trade.</b> Consuming on a wrong password
/// would let anyone who merely knows the address burn all eight codes and lock the account out of its own recovery
/// path — a denial of service against the exact mechanism AC-8.2 exists to guarantee. So: password, then consume,
/// then everything else.</para>
/// </summary>
public record RedeemPlatformRecoveryCodeCommand(string Email, string Password, string RecoveryCode)
    : IRequest<Result<PlatformSessionDto>>;

public class RedeemPlatformRecoveryCodeCommandHandler
    : IRequestHandler<RedeemPlatformRecoveryCodeCommand, Result<PlatformSessionDto>>
{
    private readonly IPlatformAccountRepository _accounts;
    private readonly IPlatformAuthService _auth;
    private readonly IUnitOfWork _unitOfWork;

    public RedeemPlatformRecoveryCodeCommandHandler(
        IPlatformAccountRepository accounts,
        IPlatformAuthService auth,
        IUnitOfWork unitOfWork)
    {
        _accounts = accounts;
        _auth = auth;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PlatformSessionDto>> Handle(
        RedeemPlatformRecoveryCodeCommand request, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetByEmailAsync(
            EmailNormalization.Normalize(request.Email ?? string.Empty), cancellationToken);

        if (account is null)
        {
            return Refuse(PlatformAuthRefusals.InvalidCredentials);
        }

        if (account.IsLockedOut())
        {
            return Refuse(PlatformAuthRefusals.TooManyAttempts);
        }

        var verification = _auth.VerifyPassword(account.PasswordHash, request.Password ?? string.Empty);
        if (verification == PasswordVerificationOutcome.Failed)
        {
            account.RecordFailedLogin();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Refuse(PlatformAuthRefusals.InvalidCredentials);
        }

        if (!account.ConsumeRecoveryCode(request.RecoveryCode ?? string.Empty))
        {
            account.RecordFailedLogin();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Refuse(PlatformAuthRefusals.InvalidCredentials);
        }

        // Spent, and persisted before anything below can refuse — AC-1.3b.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (!account.IsActive)
        {
            return Refuse(PlatformAuthRefusals.AccountDisabled);
        }

        if (verification == PasswordVerificationOutcome.SuccessNeedsRehash)
        {
            account.UpgradePasswordHash(_auth.HashPassword(request.Password!));
        }

        account.RecordSuccessfulLogin();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var token = _auth.GenerateToken(account);
        return Result<PlatformSessionDto>.Success(
            new PlatformSessionDto(token.AccessToken, token.ExpiresAtUtc, account.UnusedRecoveryCodeCount));
    }

    private static Result<PlatformSessionDto> Refuse(string code) =>
        Result<PlatformSessionDto>.Failure(PlatformAuthRefusals.MessageFor(code)!, code);
}
