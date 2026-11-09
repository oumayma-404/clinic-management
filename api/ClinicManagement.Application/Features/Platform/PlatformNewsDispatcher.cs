using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>Sends a recorded news broadcast to every address still pending. Run by the API's background jobs.</summary>
public interface IPlatformNewsDispatcher
{
    Task DispatchAsync(Guid broadcastId, CancellationToken cancellationToken);

    /// <summary>Resumes every broadcast left unfinished (a crash, exhausted retries) — the sweep's entry point.</summary>
    Task ResumeUnfinishedAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One address at a time: each recipient is claimed (saved) before its e-mail leaves, so two runs or a restart can
/// never mail the same address twice — a claim nobody confirmed ends as failed, not re-sent.
/// </summary>
public class PlatformNewsDispatcher : IPlatformNewsDispatcher
{
    /// <summary>Far past one SMTP attempt (20 s): a claim this old belongs to a run that died.</summary>
    public static readonly TimeSpan ClaimLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Leaves a just-recorded broadcast to the job its own request queued.</summary>
    public static readonly TimeSpan ResumeAfter = TimeSpan.FromMinutes(2);

    private readonly IPlatformNewsRepository _news;
    private readonly ITransactionalEmailSender _email;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PlatformNewsDispatcher> _logger;

    public PlatformNewsDispatcher(
        IPlatformNewsRepository news,
        ITransactionalEmailSender email,
        IPublicAppUrlProvider appUrl,
        IUnitOfWork unitOfWork,
        ILogger<PlatformNewsDispatcher> logger)
    {
        _news = news;
        _email = email;
        _appUrl = appUrl;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task ResumeUnfinishedAsync(CancellationToken cancellationToken)
    {
        var ids = await _news.ListUnfinishedIdsAsync(DateTime.UtcNow - ResumeAfter, cancellationToken);
        foreach (var id in ids)
        {
            try
            {
                await DispatchAsync(id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The context may hold the failed write: stop here, the next sweep picks up where this one stopped.
                _logger.LogError(ex, "Resuming news broadcast {BroadcastId} failed; retried on the next sweep", id);
                return;
            }
        }
    }

    public async Task DispatchAsync(Guid broadcastId, CancellationToken cancellationToken)
    {
        var broadcast = await _news.GetWithRecipientsAsync(broadcastId, cancellationToken);
        if (broadcast is null)
        {
            _logger.LogWarning("News broadcast {BroadcastId} not found; nothing to send", broadcastId);
            return;
        }

        var now = DateTime.UtcNow;
        if (broadcast.ExpireStaleClaims(now - ClaimLifetime, now) > 0)
        {
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        var draft = PlatformNewsDraft.From(broadcast);
        var appLink = PlatformNewsEmail.AppLink(_appUrl);

        foreach (var recipient in broadcast.PendingRecipients())
        {
            cancellationToken.ThrowIfCancellationRequested();

            broadcast.Claim(recipient.Id, DateTime.UtcNow);
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (ConflictException)
            {
                _unitOfWork.StopTracking(recipient);
                _logger.LogInformation("News broadcast {BroadcastId} is being sent by another run; this one stops", broadcastId);
                return;
            }

            var content = PlatformNewsEmail.Compose(draft, recipient.FullName, recipient.ClinicName, appLink);
            TransactionalEmailResult sent;
            try
            {
                sent = await _email.SendAsync(recipient.Email, broadcast.Subject, content, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sent = TransactionalEmailResult.Failed(ex.Message);
            }

            if (sent.Outcome == TransactionalEmailOutcome.Sent)
            {
                broadcast.RecordSent(recipient.Id, DateTime.UtcNow);
            }
            else
            {
                _logger.LogWarning(
                    "News broadcast {BroadcastId}: recipient {RecipientId} not sent ({Outcome}) {Reason}",
                    broadcastId, recipient.Id, sent.Outcome, sent.Error);
                broadcast.RecordFailed(
                    recipient.Id,
                    sent.Outcome == TransactionalEmailOutcome.NotConfigured
                        ? PlatformNewsRules.EmailNotConfigured
                        : sent.Error ?? "Envoi refusé.",
                    DateTime.UtcNow);
            }

            // The e-mail already left: its outcome is recorded even during a shutdown.
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        if (broadcast.IsSettled && broadcast.CompletedAtUtc is null)
        {
            broadcast.Complete(DateTime.UtcNow);
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            _logger.LogInformation("News broadcast {BroadcastId} finished", broadcastId);
        }
    }
}
