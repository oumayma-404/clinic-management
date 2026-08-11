using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Platform.Auth;

/// <summary>
/// Binds the second factor the bootstrap verb issued, once, by proving a code generated from it (AC-1.3a).
///
/// <para><b>Why enrolment is a separate action from sign-in and carries the password again.</b> The secret is
/// issued out of band, so at this point the account has a password and an <i>unconfirmed</i> secret — there is no
/// session to authenticate with, and there must not be one, because a password-only session is exactly what the
/// factor exists to stop being sufficient (EC-2). So the password is presented here alongside a code, and the
/// pair is what authorises the binding.</para>
///
/// <para>⚠️ <b>Nothing is bound on a failed code</b> (the spec's 400): the recovery codes are minted only after
/// the code verifies, so a wrong attempt leaves the account exactly as it was and can simply be retried. Minting
/// first and rolling back would be the same outcome on the happy path and a set of live codes nobody was shown on
/// the unhappy one.</para>
/// </summary>
public record EnrolPlatformTotpCommand(string Email, string Password, string TotpCode)
    : IRequest<Result<PlatformEnrolmentDto>>;

public class EnrolPlatformTotpCommandHandler
    : IRequestHandler<EnrolPlatformTotpCommand, Result<PlatformEnrolmentDto>>
{
    private readonly IPlatformAccountRepository _accounts;
    private readonly IPlatformAuthService _auth;
    private readonly IPlatformSecretProtector _protector;
    private readonly ITotpService _totp;
    private readonly IUnitOfWork _unitOfWork;

    public EnrolPlatformTotpCommandHandler(
        IPlatformAccountRepository accounts,
        IPlatformAuthService auth,
        IPlatformSecretProtector protector,
        ITotpService totp,
        IUnitOfWork unitOfWork)
    {
        _accounts = accounts;
        _auth = auth;
        _protector = protector;
        _totp = totp;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PlatformEnrolmentDto>> Handle(
        EnrolPlatformTotpCommand request, CancellationToken cancellationToken)
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

        if (_auth.VerifyPassword(account.PasswordHash, request.Password ?? string.Empty)
            == PasswordVerificationOutcome.Failed)
        {
            account.RecordFailedLogin();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Refuse(PlatformAuthRefusals.InvalidCredentials);
        }

        if (!account.IsActive)
        {
            return Refuse(PlatformAuthRefusals.AccountDisabled);
        }

        if (account.IsTotpEnrolled)
        {
            return Refuse(PlatformAuthRefusals.TotpAlreadyEnrolled);
        }

        if (string.IsNullOrEmpty(account.ProtectedTotpSecret)
            || !_protector.TryUnprotect(account.ProtectedTotpSecret, out var secret))
        {
            // No secret issued, or one this deployment can no longer read: both are « ask the operator to
            // re-issue », and neither is a statement about the code that was typed.
            return Refuse(PlatformAuthRefusals.TotpEnrolmentRequired);
        }

        if (!_totp.VerifyCode(secret, request.TotpCode ?? string.Empty))
        {
            account.RecordFailedLogin();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Refuse(PlatformAuthRefusals.TotpInvalid);
        }

        var codes = Enumerable.Range(0, PlatformRecoveryCode.CountPerEnrolment)
            .Select(_ => PlatformRecoveryCode.NewCode())
            .ToList();

        account.CompleteTotpEnrolment(codes);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<PlatformEnrolmentDto>.Success(new PlatformEnrolmentDto(codes));
    }

    private static Result<PlatformEnrolmentDto> Refuse(string code) =>
        Result<PlatformEnrolmentDto>.Failure(PlatformAuthRefusals.MessageFor(code)!, code);
}
