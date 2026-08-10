using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Platform.Auth;

/// <summary>
/// A signed-in console account changes <b>its own</b> password — the only account action reachable over the web
/// (AC-8.6). Creating and deactivating are the bootstrap verb's (AC-8.5).
///
/// <para><b>The account comes from the session, never from the request.</b> There is no e-mail field: an id in the
/// body would make this « change any console account's password », and with two or three accounts holding
/// cross-cabinet read access that is not a distinction worth leaving to a policy attribute.</para>
///
/// <para>⚠️ <b>It also clears <c>MustChangePassword</c>, which is what makes the printed one-time password
/// genuinely one-time</b> (AC-8.1). <c>PlatformAccountStateMiddleware</c> refuses every other console route while
/// that flag is set, so this command is the only thing a freshly-bootstrapped account can do — and the change is
/// what lets it do anything else.</para>
/// </summary>
public record ChangePlatformPasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result>;

public class ChangePlatformPasswordCommandHandler : IRequestHandler<ChangePlatformPasswordCommand, Result>
{
    private readonly IPlatformSessionContext _session;
    private readonly IPlatformAccountRepository _accounts;
    private readonly IPlatformAuthService _auth;
    private readonly IUnitOfWork _unitOfWork;

    public ChangePlatformPasswordCommandHandler(
        IPlatformSessionContext session,
        IPlatformAccountRepository accounts,
        IPlatformAuthService auth,
        IUnitOfWork unitOfWork)
    {
        _session = session;
        _accounts = accounts;
        _auth = auth;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ChangePlatformPasswordCommand request, CancellationToken cancellationToken)
    {
        var accountId = _session.GetAccountId();
        if (accountId is null)
        {
            return Refuse(PlatformAuthRefusals.NoSession);
        }

        var account = await _accounts.GetForStateCheckAsync(accountId.Value, cancellationToken);
        if (account is null || !account.IsActive)
        {
            return Refuse(PlatformAuthRefusals.NoSession);
        }

        if (_auth.VerifyPassword(account.PasswordHash, request.CurrentPassword ?? string.Empty)
            == PasswordVerificationOutcome.Failed)
        {
            return Refuse(PlatformAuthRefusals.InvalidCredentials);
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword)
            || request.NewPassword.Length < PasswordPolicy.MinLength)
        {
            return Refuse(PlatformAuthRefusals.PasswordPolicy);
        }

        // SetPassword bumps TokenVersion, so the token in the caller's own hands dies too — deliberate, and the
        // reason the console client signs back in after a change rather than carrying on with a stale credential.
        account.SetPassword(_auth.HashPassword(request.NewPassword), mustChangePassword: false);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Result Refuse(string code) =>
        Result.Failure(PlatformAuthRefusals.MessageFor(code)!, code);
}
