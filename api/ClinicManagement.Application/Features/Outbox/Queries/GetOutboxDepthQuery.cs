using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Outbox.Queries;

/// <summary>
/// Admin-only: how deep the clinic's three background queues are — reminders, El Fatoora e-invoices, document
/// emails (multi-tenant-cloud US-6).
///
/// <para><b>Why this exists at all.</b> <c>/hangfire</c> is loopback-only in <i>every</i> profile, and behind a
/// reverse proxy every request's <c>RemoteIpAddress</c> is the proxy container — correct as security, total as
/// blindness. Meanwhile the story's own R-1 says the tenant-scope failure mode is <b>silence</b>: a job that never
/// declared a scope reads nothing and logs a clean run, so reminders simply stop and every screen looks fine. A
/// queue depth with an age on it is the one reading that separates « nothing to send » from « nothing is
/// sending ».</para>
///
/// <para><b>Deliberately per-clinic, not per-install.</b> It is reached with a clinic admin's token, so an
/// install-wide figure would hand one practice a fact about another's workload. It costs nothing in detection: a
/// dispatcher that has stopped stops for every clinic at once, so any one clinic's admin sees it.</para>
///
/// <para><b>Deliberately no frontend caller, unlike an unwired UI capability.</b> Its consumer is the operator —
/// <c>curl</c> with an admin token, or whatever polls it — the same class of consumer as <c>/health</c>. That is
/// why it returns numbers and instants rather than French labels.</para>
/// </summary>
public class GetOutboxDepthQuery : IRequest<Result<OutboxDepthDto>>
{
    /// <summary>
    /// How far back <see cref="ReminderOutboxDepthDto.FailedRecent"/> looks. Days rather than « today » for the
    /// reason <c>ReminderLogCounts</c> gives: a send that failed at 23:00 would otherwise vanish from the counter
    /// at midnight, before anyone came in to see it.
    /// </summary>
    public const int FailedWindowDays = 7;
}

public class GetOutboxDepthQueryHandler : IRequestHandler<GetOutboxDepthQuery, Result<OutboxDepthDto>>
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IDocumentEmailRepository _documentEmailRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly ILogger<GetOutboxDepthQueryHandler> _logger;

    public GetOutboxDepthQueryHandler(
        INotificationRepository notificationRepository,
        IInvoiceRepository invoiceRepository,
        IDocumentEmailRepository documentEmailRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        ILogger<GetOutboxDepthQueryHandler> logger)
    {
        _notificationRepository = notificationRepository;
        _invoiceRepository = invoiceRepository;
        _documentEmailRepository = documentEmailRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _logger = logger;
    }

    public async Task<Result<OutboxDepthDto>> Handle(
        GetOutboxDepthQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var callerId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(callerId))
            {
                return Result<OutboxDepthDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var caller = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
            if (caller == null)
            {
                return Result<OutboxDepthDto>.Failure("Utilisateur introuvable.");
            }

            if (!caller.IsAdmin())
            {
                return Result<OutboxDepthDto>.Failure(
                    "Seuls les administrateurs peuvent consulter l'état des files d'attente.");
            }

            // ONE instant for all three queues. Reading the clock per queue would let two of the three « due »
            // figures be measured against different moments, and the whole value of this read is comparing them
            // with each other and with the reading taken five minutes ago.
            var now = DateTime.UtcNow;
            var failedSince = now.AddDays(-GetOutboxDepthQuery.FailedWindowDays);

            // Sequential, not Task.WhenAll: the three repositories share the request's DbContext, which a
            // concurrent read throws on — the same constraint the dashboard's section readers document.
            var reminders = await _notificationRepository.GetOutboxDepthAsync(
                caller.ClinicId, now, failedSince, cancellationToken);

            var eInvoices = await _invoiceRepository.GetEInvoiceOutboxDepthAsync(
                caller.ClinicId, now, cancellationToken);

            var documentEmails = await _documentEmailRepository.GetOutboxDepthAsync(
                caller.ClinicId, cancellationToken);

            return Result<OutboxDepthDto>.Success(new OutboxDepthDto
            {
                MeasuredAtUtc = now,
                Reminders = new ReminderOutboxDepthDto
                {
                    Pending = reminders.Pending,
                    Due = reminders.Due,
                    Blocked = reminders.Blocked,
                    FailedRecent = reminders.FailedRecent,
                    FailedSinceUtc = failedSince,
                    OldestDueScheduledForUtc = reminders.OldestDueScheduledFor
                },
                EInvoices = new EInvoiceOutboxDepthDto
                {
                    Queued = eInvoices.Queued,
                    Due = eInvoices.Due,
                    Failed = eInvoices.Failed,
                    OldestDueNextAttemptUtc = eInvoices.OldestDueNextAttemptAt
                },
                DocumentEmails = new DocumentEmailOutboxDepthDto
                {
                    Queued = documentEmails.Queued,
                    Blocked = documentEmails.Blocked,
                    Failed = documentEmails.Failed,
                    OldestQueuedUtc = documentEmails.OldestQueuedAt
                }
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // The irony would be load-bearing without this line: this endpoint exists because « a job with no tenant
            // scope reads nothing and logs a clean run », and a broken read here used to become a French sentence
            // with no trace anywhere — turning the one diagnostic endpoint into a second silent failure.
            _logger.LogError(ex, "Could not read the outbox depth for the caller's clinic");
            return Result<OutboxDepthDto>.Failure(
                "Erreur lors de la récupération de l'état des files d'attente.");
        }
    }
}
