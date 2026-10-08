using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.API.Startup;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicManagement.API.Controllers;

/// <summary>« Paramètres → PC de secours »: the clinic admin's side (<c>clinic-pc-copy</c>). Absent where no change log is published.</summary>
[ApiController]
[Route("api/relay")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[AllowedWhileCloudFenced("Managing the PC de secours itself — retiring it is how an administrator frees a cabinet whose PC fell silent.")]
public class RelayController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;
    private readonly IStepUpConfirmations _stepUp;
    private readonly IClinicContext _clinicContext;

    public RelayController(
        IMediator mediator, DeploymentProfile deployment, IStepUpConfirmations stepUp, IClinicContext clinicContext)
    {
        _mediator = mediator;
        _deployment = deployment;
        _stepUp = stepUp;
        _clinicContext = clinicContext;
    }

    [HttpGet("status")]
    public async Task<ActionResult<RelayStatusDto>> Status(CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetRelayStatusQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result.Code)) : Ok(result.Value);
    }

    [OnlineOnly("Managing the PC de secours is the cloud's; a copy cannot pair, retire or declare one.")]
    [HttpPost("pairing-codes")]
    [AllowsWithoutSubscription("A PC that will hold the clinic's own records may always be set up to copy them.")]
    public async Task<ActionResult<RelayPairingCodeDto>> IssuePairingCode(
        [FromBody] IssueRelayPairingCodeRequest request,
        [FromHeader(Name = BackupController.StepUpHeader)] string? confirmation,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var callerId = _clinicContext.GetUserId();
        if (string.IsNullOrWhiteSpace(callerId) || !_stepUp.Consume(callerId, RelayStepUpActions.Pairing, confirmation ?? string.Empty))
        {
            return Failure("Cette action demande une confirmation récente de votre identité. Veuillez réessayer.",
                StatusCodes.Status403Forbidden);
        }

        var result = await _mediator.Send(new IssueRelayPairingCodeCommand(request.Label ?? string.Empty), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>
    /// The Windows app gives back a code its installer never presented (AC-1.11): Windows' prompt refused, too little
    /// room, a failed download. The code is the credential — the credentials door has no session to send — and every
    /// outcome is the same 204, so it reveals nothing about any clinic.
    /// </summary>
    [OnlineOnly("Managing the PC de secours is the cloud's; a copy cannot pair, retire or declare one.")]
    [HttpPost("pairing-codes/release")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RelayTokenPolicy)]
    [AllowsWithoutSubscription("Giving back an unused setup code frees the clinic's place; it records no new work.")]
    public async Task<IActionResult> ReleasePairingCode(
        [FromBody] ReleaseRelayPairingCodeRequest request, CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        await _mediator.Send(new ReleaseRelayPairingCodeCommand(request.Code ?? string.Empty), cancellationToken);
        return NoContent();
    }

    [OnlineOnly("Managing the PC de secours is the cloud's; a copy cannot pair, retire or declare one.")]
    [HttpDelete]
    [AllowsWithoutSubscription("Retiring a PC that holds the clinic's records is offboarding, never new work.")]
    public async Task<ActionResult<RelayStatusDto>> Retire(CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new RetireRelayCommand(), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>
    /// « Déclarer perdu ou volé » (AC-8.4) — admin + a fresh confirmation of identity, like pairing: it resets every
    /// account of the cabinet, so a stolen session alone must not be able to do it.
    /// </summary>
    [OnlineOnly("Managing the PC de secours is the cloud's; a copy cannot pair, retire or declare one.")]
    [HttpPost("lost")]
    [AllowsWithoutSubscription("Securing the accounts a stolen PC held is never new work, and an unpaid cabinet must be able to.")]
    public async Task<ActionResult<RelayStatusDto>> DeclareLost(
        [FromHeader(Name = BackupController.StepUpHeader)] string? confirmation,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var callerId = _clinicContext.GetUserId();
        if (string.IsNullOrWhiteSpace(callerId) || !_stepUp.Consume(callerId, RelayStepUpActions.Lost, confirmation ?? string.Empty))
        {
            return Failure("Cette action demande une confirmation récente de votre identité. Veuillez réessayer.",
                StatusCodes.Status403Forbidden);
        }

        var result = await _mediator.Send(new DeclareRelayLostCommand(), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>
    /// « Reprendre la main » (US-7, AC-7.1/7.2) — admin + a fresh confirmation of identity: what the cabinet recorded on
    /// the PC since the lock will not come back by itself, so a stolen session alone must not be able to do it.
    /// </summary>
    [OnlineOnly("Managing the PC de secours is the cloud's; a copy cannot pair, retire or declare one.")]
    [HttpPost("reclaim")]
    [AllowsWithoutSubscription("Taking a locked cabinet's saves back from a silent PC records nothing new, and an unpaid cabinet must be able to.")]
    public async Task<ActionResult<RelayStatusDto>> Reclaim(
        [FromHeader(Name = BackupController.StepUpHeader)] string? confirmation,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var callerId = _clinicContext.GetUserId();
        if (string.IsNullOrWhiteSpace(callerId) || !_stepUp.Consume(callerId, RelayStepUpActions.Reclaim, confirmation ?? string.Empty))
        {
            return Failure("Cette action demande une confirmation récente de votre identité. Veuillez réessayer.",
                StatusCodes.Status403Forbidden);
        }

        var result = await _mediator.Send(new ReclaimRelayCommand(), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>« Modifications à vérifier » (D18, AC-5.6): what the return found the cloud had too, newest first.</summary>
    [HttpGet("review-items")]
    public async Task<ActionResult<PagedResult<RelayReviewItemDto>>> ReviewItems(
        [FromQuery] bool includeReviewed, [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetRelayReviewItemsQuery(includeReviewed, page, pageSize), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result.Code)) : Ok(result.Value);
    }

    /// <summary>« Vu »: an admin read both versions. Changes no record.</summary>
    [OnlineOnly("The return's review list is the cloud's; a copy holds none.")]
    [HttpPost("review-items/{id:guid}/seen")]
    [AllowsWithoutSubscription("Marking a line of the return's list as read records no new work.")]
    public async Task<ActionResult<RelayReviewItemDto>> MarkReviewItemSeen(Guid id, CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new MarkRelayReviewItemSeenCommand(id), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result.Code)) : Ok(result.Value);
    }

    internal static int StatusFor(string? code) => code switch
    {
        MarkRelayReviewItemSeenCommandHandler.NotFoundCode => StatusCodes.Status404NotFound,
        RelayRefusals.NotHoldingCode => StatusCodes.Status409Conflict,
        RelayRefusals.AlreadyPairedCode => StatusCodes.Status409Conflict,
        RelayRefusals.PairingCodeExpiredCode => StatusCodes.Status410Gone,
        RelayRefusals.NoRelayCode => StatusCodes.Status404NotFound,
        RelayRefusals.NotAdminCode => StatusCodes.Status403Forbidden,
        RelayRefusals.UnknownRelayCode => StatusCodes.Status401Unauthorized,
        RelayRefusals.RetiredCode => StatusCodes.Status410Gone,
        RelayRefusals.VersionMismatchCode => StatusCodes.Status409Conflict,
        RelayRefusals.NotRetiredCode => StatusCodes.Status409Conflict,
        // The password and the code were right; something else is owed first (AC-1.5).
        RelayRefusals.AuthenticatorRequiredCode => StatusCodes.Status403Forbidden,
        RelayRefusals.PasswordChangeRequiredCode => StatusCodes.Status403Forbidden,
        "relay_blob_not_found" => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };
}

public sealed record IssueRelayPairingCodeRequest(string? Label);

public sealed record ReleaseRelayPairingCodeRequest(string? Code);
