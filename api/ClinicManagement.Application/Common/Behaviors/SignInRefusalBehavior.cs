using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.SignIn;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common.Behaviors;

/// <summary>
/// Writes down every refused sign-in at the six credential doors, and lets <see cref="ISignInRefusalRecorder"/> decide
/// whether the vendor is told.
///
/// <para><b>A behavior and not a call in each handler</b>, for <see cref="RealtimeBroadcastBehavior{TRequest,TResponse}"/>'s
/// reason: a door is covered by carrying <see cref="ICredentialAttempt"/>, so a seventh cannot be the one that forgets —
/// and <c>SignInRefusalCoverageTests</c> fails on a request that checks a password anonymously without it.</para>
///
/// <para>⚠️ It reads the refusal off the <see cref="Result.Code"/> the handler already returned and changes nothing
/// about the answer. The account is looked up only for a code that is a refusal, so the ordinary « code requis » step
/// of every sign-in costs nothing.</para>
/// </summary>
public class SignInRefusalBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Decided once per closed generic type (TRequest is fixed per behavior instantiation).
    private static readonly bool IsCredentialDoor = typeof(ICredentialAttempt).IsAssignableFrom(typeof(TRequest));

    private readonly IUserRepository _users;
    private readonly IPlatformAccountRepository _consoleAccounts;
    private readonly ISignInRefusalRecorder _recorder;
    private readonly ILogger<SignInRefusalBehavior<TRequest, TResponse>> _logger;

    public SignInRefusalBehavior(
        IUserRepository users,
        IPlatformAccountRepository consoleAccounts,
        ISignInRefusalRecorder recorder,
        ILogger<SignInRefusalBehavior<TRequest, TResponse>> logger)
    {
        _users = users;
        _consoleAccounts = consoleAccounts;
        _recorder = recorder;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next();

        if (IsCredentialDoor
            && request is ICredentialAttempt attempt
            && response is Result { IsFailure: true, Code: { } code }
            && SignInRefusalRules.IsRecorded(code))
        {
            // Recording must never cost the caller their answer.
            try
            {
                var refusal = await DescribeAsync(attempt, code);
                if (refusal is not null)
                {
                    _recorder.Record(refusal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A refused sign-in could not be recorded ({Request}).", typeof(TRequest).Name);
            }
        }

        return response;
    }

    // CancellationToken.None: a caller who hung up after being refused is still a refusal worth counting.
    private async Task<SignInRefusal?> DescribeAsync(ICredentialAttempt attempt, string code)
    {
        var emailBlank = string.IsNullOrWhiteSpace(attempt.Email);
        var emailKey = emailBlank ? string.Empty : EmailNormalization.Normalize(attempt.Email!);

        var accountExists = false;
        var pendingActivation = false;
        string? accountLabel = null;

        if (!emailBlank)
        {
            if (attempt.Door.IsConsole())
            {
                var account = await _consoleAccounts.GetByEmailAsync(emailKey, CancellationToken.None);
                accountExists = account is not null;
                accountLabel = accountExists ? SignInRefusalRules.ConsoleAccountLabel : null;
            }
            else
            {
                var user = await _users.GetByEmailAsync(emailKey, CancellationToken.None);
                accountExists = user is not null;
                pendingActivation = user?.IsPendingActivation == true;
                accountLabel = user is null ? null : SignInRefusalRules.AccountLabel(user.Clinic?.Name, user.Role);
            }
        }

        var reason = SignInRefusalRules.Classify(code, emailBlank, accountExists, pendingActivation);
        return reason is null
            ? null
            : new SignInRefusal(
                attempt.Door, reason.Value, SignInRefusalRules.ShownEmail(attempt.Email), emailKey, accountLabel,
                DateTime.UtcNow);
    }
}
