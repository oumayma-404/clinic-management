using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.News;
using ClinicManagement.Application.Features.News.Commands;
using ClinicManagement.Application.Features.News.Queries;
using ClinicManagement.Infrastructure.Deployment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// « Nouveautés » in the app: the news the vendor e-mailed, kept on a page for the cabinet's administrators.
///
/// <para>⚠️ <b>404 before the mediator where the deployment serves no vendor console</b> — no news is ever written
/// there, and the absent route is what tells the browser to show no « Nouveautés » row (FeedbackController's rule).</para>
/// </summary>
[ApiController]
[Route("api/news")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class NewsController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;

    public NewsController(IMediator mediator, DeploymentProfile deployment)
    {
        _mediator = mediator;
        _deployment = deployment;
    }

    [HttpGet]
    public async Task<ActionResult<NewsFeedDto>> List(CancellationToken cancellationToken)
    {
        if (!_deployment.ServesPlatformConsole) return NotFound();

        var result = await _mediator.Send(new GetNewsQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<NewsSummaryDto>> Summary(CancellationToken cancellationToken)
    {
        if (!_deployment.ServesPlatformConsole) return NotFound();

        var result = await _mediator.Send(new GetNewsSummaryQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [HttpPost("read")]
    [AllowsWithoutSubscription("One admin's own read marker on the vendor's news, not clinic work.")]
    public async Task<IActionResult> MarkRead(CancellationToken cancellationToken)
    {
        if (!_deployment.ServesPlatformConsole) return NotFound();

        var result = await _mediator.Send(new MarkNewsReadCommand(), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }
}
