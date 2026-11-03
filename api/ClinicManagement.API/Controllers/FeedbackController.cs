using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Feedback.Commands;
using ClinicManagement.Application.Features.Feedback.Queries;
using ClinicManagement.Infrastructure.Deployment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// « Donner mon avis ou une idée » and the week-one question (guided-first-days E). Every role.
///
/// <para>⚠️ <b>404 before the mediator where the deployment serves no vendor console</b>: the messages go to the
/// console team and are read in its « Retours », so on a clinic's own PC there is nobody to send them to — and an
/// absent route is what tells the browser to show neither the card nor the « Aide » row.</para>
/// </summary>
[ApiController]
[Route("api/feedback")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class FeedbackController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;

    public FeedbackController(IMediator mediator, DeploymentProfile deployment)
    {
        _mediator = mediator;
        _deployment = deployment;
    }

    /// <summary>Whether to put the week-one question to the signed-in person now.</summary>
    [HttpGet("prompt")]
    public async Task<ActionResult<FeedbackPromptDto>> GetPrompt(CancellationToken cancellationToken)
    {
        if (!_deployment.ServesPlatformConsole) return NotFound();

        var result = await _mediator.Send(new GetFeedbackPromptQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>The card appeared (<c>shown</c>), « Plus tard » (<c>later</c>) or « Ne plus demander » (<c>never</c>).</summary>
    [HttpPost("prompt")]
    [AllowsWithoutSubscription("One person's own question state, not clinic work — an expired cabinet may still close it.")]
    public async Task<IActionResult> SetPrompt([FromBody] SetFeedbackPromptCommand command, CancellationToken cancellationToken)
    {
        if (!_deployment.ServesPlatformConsole) return NotFound();

        var result = await _mediator.Send(command ?? new SetFeedbackPromptCommand(), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : NoContent();
    }

    /// <summary>One message to the vendor. E-mailed to the console team at once and kept in « Retours ».</summary>
    [HttpPost]
    [AllowsWithoutSubscription("A message to the vendor, not clinic work — an expired cabinet saying why it left is the answer most wanted.")]
    public async Task<ActionResult<FeedbackReceiptDto>> Submit(
        [FromBody] SubmitFeedbackCommand command, CancellationToken cancellationToken)
    {
        if (!_deployment.ServesPlatformConsole) return NotFound();

        var result = await _mediator.Send(command ?? new SubmitFeedbackCommand(), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
