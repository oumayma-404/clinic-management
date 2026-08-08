using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Outbox.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Queue depth for the clinic's two background outboxes — reminders and document emails
/// (multi-tenant-cloud US-6).
///
/// <para><b>It exists because the Hangfire dashboard cannot be reached where it is most needed.</b>
/// <c>/hangfire</c> is loopback-only in <i>both</i> auth modes, and behind a reverse proxy every request's
/// <c>RemoteIpAddress</c> is the proxy container — so in a hosted deployment nobody can see whether the jobs are
/// draining. That matters more than it sounds: the tenant-scope work's stated risk (R-1) is that a job which never
/// declared a scope reads **nothing** and logs a clean run, so reminders stop going out while every screen in the
/// product looks perfectly normal.</para>
///
/// <para>Read-only, and there is no action that retries or clears a row. Draining a queue is the dispatchers'
/// job; a button that re-queued by hand would be a second write path into an outbox whose whole design is that
/// one thing decides what sends.</para>
/// </summary>
[ApiController]
[Route("api/outbox")]
// The owner's question (« est-ce que les rappels partent ? »), and the answer aggregates the whole clinic's
// queues — the same class of clinic-wide operational read as the audit ledger and the backup history.
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class OutboxController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public OutboxController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// The three queues' depths, each with the age of its oldest waiting row. <b>The age is the diagnosis</b>:
    /// a depth alone cannot tell three reminders enqueued a second ago from three stuck since yesterday.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<OutboxDepthDto>> GetOutboxDepth(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetOutboxDepthQuery(), cancellationToken);

        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
