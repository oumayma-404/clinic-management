using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Auth;

/// <summary>
/// Console sign-in: e-mail, password <b>and</b> a one-time code (AC-1.2).
///
/// <para><b>The order of the checks is the security property.</b> Lockout before the password (so a locked account
/// is not a password oracle), the password before the enrolment state (so « ce compte doit enrôler » cannot be used
/// to enumerate accounts), and the enrolment state before the code (so an unenrolled account gets the spec's 403
/// rather than a refusal about a code it has no way to produce). Each of those is a case in
/// <c>PlatformAuthTests</c>.</para>
///
/// <para>⚠️ <b>A missing code and a wrong code are different refusals, and only one of them is safe.</b> The spec
/// gives a bare omission its own <c>totp_required</c>, because that is a client mistake with no attacker value —
/// but a code that is <i>present and wrong</i> collapses into <see cref="PlatformAuthRefusals.InvalidCredentials"/>
/// with the wrong-password case, or the endpoint reports which half of the credential was right.</para>
///
/// <para>⚠️ <b>This lives in <c>Features/Platform/Auth</c> and not <c>Features/Platform/Commands</c>.</b>
/// <c>RealtimeResourceResolver</c> derives its broadcast key from the namespace, so a sign-in placed under
/// <c>.Commands</c> would announce a <c>platform</c> resource nothing subscribes to — failing
/// <c>RealtimeResourceResolverTests</c> — and would broadcast a console login into a clinic's group besides.</para>
/// </summary>
public record PlatformLoginCommand(string Email, string Password, string? TotpCode)
    : IRequest<Result<PlatformSessionDto>>;

public class PlatformLoginCommandHandler
    : IRequestHandler<PlatformLoginCommand, Result<PlatformSessionDto>>
{
    private readonly IPlatformAccountRepository _accounts;
    private readonly IPlatformAuthService _auth;
    private readonly IPlatformSecretProtector _protector;
    private readonly ITotpService _totp;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PlatformLoginCommandHandler> _logger;

    public PlatformLoginCommandHandler(
        IPlatformAccountRepository accounts,
        IPlatformAuthService auth,
        IPlatformSecretProtector protector,
        ITotpService totp,
        IUnitOfWork unitOfWork,
        ILogger<PlatformLoginCommandHandler> logger)
    {
        _accounts = accounts;
        _auth = auth;
        _protector = protector;
        _totp = totp;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<PlatformSessionDto>> Handle(
        PlatformLoginCommand request, CancellationToken cancellationToken)
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

        // Disclosed only after the password is known correct — the same line the clinic login draws.
        if (!account.IsActive)
        {
            return Refuse(PlatformAuthRefusals.AccountDisabled);
        }

        if (!account.IsTotpEnrolled)
        {
            return Refuse(PlatformAuthRefusals.TotpEnrolmentRequired);
        }

        if (string.IsNullOrWhiteSpace(request.TotpCode))
        {
            return Refuse(PlatformAuthRefusals.TotpRequired);
        }

        if (!VerifyTotp(account, request.TotpCode))
        {
            account.RecordFailedLogin();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Refuse(PlatformAuthRefusals.InvalidCredentials);
        }

        if (verification == PasswordVerificationOutcome.SuccessNeedsRehash)
        {
            account.UpgradePasswordHash(_auth.HashPassword(request.Password!));
        }

        account.RecordSuccessfulLogin();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var token = _auth.GenerateToken(account);
        return Result<PlatformSessionDto>.Success(new PlatformSessionDto(token.AccessToken, token.ExpiresAtUtc));
    }

    /// <summary>
    /// Verifies the code against the decrypted secret. An <b>undecryptable</b> secret is logged and refused —
    /// see <see cref="IPlatformSecretProtector"/> for why treating it as « no factor needed » is the one
    /// degradation this must never take.
    /// </summary>
    private bool VerifyTotp(PlatformAccount account, string code)
    {
        if (!_protector.TryUnprotect(account.ProtectedTotpSecret!, out var secret))
        {
            _logger.LogError(
                "Le secret du second facteur du compte console {AccountId} est illisible — la clé de protection " +
                "des données a probablement changé. Réémettez-le avec « platform-account --reset-totp ».",
                account.Id);
            return false;
        }

        return _totp.VerifyCode(secret, code);
    }

    private static Result<PlatformSessionDto> Refuse(string code) =>
        Result<PlatformSessionDto>.Failure(PlatformAuthRefusals.MessageFor(code)!, code);
}
