using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform.Commands;

/// <summary>Sends the news e-mail to the signed-in console account only. Records nothing.</summary>
public class SendPlatformNewsTestCommand : IRequest<Result<PlatformNewsTestSentDto>>
{
    public string? Subject { get; set; }
    public IReadOnlyList<PlatformNewsItemRequest>? Items { get; set; }
}

public class SendPlatformNewsTestCommandHandler
    : IRequestHandler<SendPlatformNewsTestCommand, Result<PlatformNewsTestSentDto>>
{
    public const string SendFailedCode = "news_test_failed";

    private readonly IPlatformAccountRepository _accounts;
    private readonly IPlatformSessionContext _session;
    private readonly ITransactionalEmailSender _email;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly ILogger<SendPlatformNewsTestCommandHandler> _logger;

    public SendPlatformNewsTestCommandHandler(
        IPlatformAccountRepository accounts,
        IPlatformSessionContext session,
        ITransactionalEmailSender email,
        IPublicAppUrlProvider appUrl,
        ILogger<SendPlatformNewsTestCommandHandler> logger)
    {
        _accounts = accounts;
        _session = session;
        _email = email;
        _appUrl = appUrl;
        _logger = logger;
    }

    public async Task<Result<PlatformNewsTestSentDto>> Handle(
        SendPlatformNewsTestCommand request, CancellationToken cancellationToken)
    {
        if (!_email.IsConfigured)
        {
            return Result<PlatformNewsTestSentDto>.Failure(
                PlatformNewsRules.EmailNotConfigured, PlatformNewsRules.EmailNotConfiguredCode);
        }

        var drafted = PlatformNewsRules.Normalize(request.Subject, request.Items);
        if (drafted.IsFailure)
        {
            return Result<PlatformNewsTestSentDto>.FailureFrom(drafted);
        }

        try
        {
            var account = await _accounts.GetByIdAsync(PlatformAccessLedger.RequireAccountId(_session), cancellationToken);
            if (account is null || string.IsNullOrWhiteSpace(account.Email))
            {
                return Result<PlatformNewsTestSentDto>.Failure("Votre compte console n'a pas d'adresse e-mail.");
            }

            var draft = drafted.Value!;
            var content = PlatformNewsEmail.Compose(
                draft, account.FullName, PlatformNewsEmail.SampleClinicName, PlatformNewsEmail.AppLink(_appUrl));

            var sent = await _email.SendAsync(
                account.Email, PlatformNewsEmail.TestSubjectPrefix + draft.Subject, content, cancellationToken);

            if (sent.Outcome != TransactionalEmailOutcome.Sent)
            {
                _logger.LogWarning("News test e-mail to console account {AccountId} not sent: {Outcome} {Reason}", account.Id, sent.Outcome, sent.Error);
                return Result<PlatformNewsTestSentDto>.Failure(
                    "L'e-mail de test n'a pas pu partir. Réessayez dans un instant.", SendFailedCode);
            }

            return Result<PlatformNewsTestSentDto>.Success(new PlatformNewsTestSentDto(account.Email));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error sending the news test e-mail");
            return Result<PlatformNewsTestSentDto>.Failure("L'e-mail de test n'a pas pu partir.", ex);
        }
    }
}
