using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.API.Models;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Platform.Queries;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// « Nouveautés » — the vendor writes the app's news and e-mails it to the admin of every active or trialling cabinet
/// (<c>console-news-broadcast</c>). Console port only, like every <c>/api/platform</c> route.
/// </summary>
[ApiController]
[Route("api/platform/news")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformNewsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PlatformNewsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlatformNewsBroadcastDto>>> List(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ListPlatformNewsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    [HttpGet("audience")]
    public async Task<ActionResult<PlatformNewsAudienceDto>> Audience(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPlatformNewsAudienceQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    /// <summary>A POST because it carries the whole form; it writes nothing.</summary>
    [HttpPost("preview")]
    [AllowsWithoutSubscription("Rendering the vendor's own news e-mail records no cabinet's work.")]
    public async Task<ActionResult<PlatformNewsPreviewDto>> Preview(
        [FromBody] PlatformNewsDraftRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new PreviewPlatformNewsQuery { Subject = request.Subject, Items = request.Items }, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    [HttpPost("test")]
    [AllowsWithoutSubscription("A test of the vendor's news goes to the vendor's own address and records nothing.")]
    public async Task<ActionResult<PlatformNewsTestSentDto>> SendTest(
        [FromBody] PlatformNewsDraftRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SendPlatformNewsTestCommand { Subject = request.Subject, Items = request.Items }, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result, StatusFor(result.Code));
    }

    [HttpPost]
    [AllowsWithoutSubscription("The vendor's news is the vendor's own mailing; it records no cabinet's work.")]
    public async Task<ActionResult<PlatformNewsQueuedDto>> Send(
        [FromBody] SendPlatformNewsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SendPlatformNewsCommand { RequestId = request.RequestId, Subject = request.Subject, Items = request.Items },
            cancellationToken);

        if (result.IsFailure)
        {
            return HandleFailure(result, StatusFor(result.Code));
        }

        var queued = result.Value!;
        // A repeated press queues nothing: the first press's job, or the resume sweep, finishes that send.
        if (!queued.AlreadyQueued)
        {
            BackgroundJob.Enqueue<PlatformNewsDispatchJob>(job => job.Dispatch(queued.BroadcastId, CancellationToken.None));
        }

        return Accepted(queued);
    }

    private static int StatusFor(string? code) => code switch
    {
        PlatformNewsRules.EmailNotConfiguredCode => StatusCodes.Status503ServiceUnavailable,
        SendPlatformNewsTestCommandHandler.SendFailedCode => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status400BadRequest,
    };
}
