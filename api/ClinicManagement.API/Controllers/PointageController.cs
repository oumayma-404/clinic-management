using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Application.Features.Team.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// The borne (<c>pointage-borne</c>): a reception device. It proves what it is with an <c>HttpOnly</c> cookie set by
/// « Ajouter une borne » — on the admin's own device, or claimed by the signed-in device she chose; each person proves
/// who she is with her schéma. The class policy keeps any future action here closed; each anonymous one is on the reviewed list.
/// </summary>
[ApiController]
[Route("api/pointage")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[AllowsBeforeClockIn("The borne is how she clocks in; refusing it would leave the lock unliftable.")]
public class PointageController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PointageController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Whether this device is a borne, and its tiles. Any device may ask; only a borne hears names.</summary>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<PointageStatusDto>> Status(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPointageStatusQuery { Secret = BorneCookie.Read(Request) }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>An arrival or a departure, proved by the schéma.</summary>
    [HttpPost("punch")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.PointagePolicy)]
    public async Task<ActionResult<PunchResultDto>> Punch([FromBody] PunchRequest request, CancellationToken cancellationToken)
    {
        var secret = BorneCookie.Read(Request);
        var result = await _mediator.Send(new PunchCommand
        {
            Secret = secret,
            UserId = request.UserId ?? string.Empty,
            Pattern = request.Pattern,
            Expected = request.Expected,
            DeclaredPreviousDeparture = request.DeclaredPreviousDeparture,
        }, cancellationToken);
        if (result.IsFailure)
            return HandleFailure(result, StatusFor(result.Code));

        if (result.Value!.Refused is { } refused)
        {
            return StatusCode(StatusFor(refused.Code), new
            {
                error = refused.Error,
                code = refused.Code,
                attemptsLeft = refused.AttemptsLeft,
                lockedUntil = refused.LockedUntil,
                day = refused.PreviousDay?.Day,
                arrival = refused.PreviousDay?.Arrival,
                lastSaveAt = refused.PreviousDay?.LastSaveAt,
            });
        }

        BorneCookie.Write(Response, Request.IsHttps, secret!);
        return Ok(result.Value.Done);
    }

    public sealed record PunchRequest(
        string? UserId, List<int>? Pattern, string? Expected = null, string? DeclaredPreviousDeparture = null);

    /// <summary>
    /// « Pointez votre arrivée » and « Pointer votre départ ? » for the signed-in account (<c>pointage-obligatoire</c>).
    /// Under <c>/api/pointage</c> because the borne cookie is scoped there: only here can « this device is a borne »
    /// be answered.
    /// </summary>
    [HttpGet("me/clock-status")]
    [Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
    public async Task<ActionResult<ClockStatusDto>> MyClockStatus(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyClockStatusQuery { Secret = BorneCookie.Read(Request) }, cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    /// <summary>
    /// The device an admin chose in « Ajouter une borne » picks it up and receives the borne's cookie. Under
    /// <c>/api/pointage</c> because that is the cookie's path.
    /// </summary>
    [HttpPost("me/claim-borne")]
    [Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
    public async Task<ActionResult<AttendanceTerminalDto>> ClaimBorne(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ClaimBorneCommand { CurrentSecret = BorneCookie.Read(Request) }, cancellationToken);
        if (result.IsFailure)
            return HandleFailure(result, StatusFor(result.Code));

        BorneCookie.Write(Response, Request.IsHttps, result.Value!.Secret);
        return Ok(result.Value.Terminal);
    }

    private static int StatusFor(string? code) => code switch
    {
        AttendanceRules.BorneNotWaitingCode => StatusCodes.Status404NotFound,
        AttendanceRules.TerminalUnknownCode => StatusCodes.Status401Unauthorized,
        AttendanceRules.SchemaLockedCode => StatusCodes.Status423Locked,
        AttendanceRules.SchemaMissingCode or AttendanceRules.OnLeaveCode or AttendanceRules.PunchStateChangedCode
            or AttendanceLockRules.PreviousDepartureMissingCode
            => StatusCodes.Status409Conflict,
        AttendanceRules.ModuleOffCode or AttendanceRules.PersonNotTrackedCode => StatusCodes.Status403Forbidden,
        SubscriptionRefusals.RequiredCode or SubscriptionRefusals.SuspendedCode or SubscriptionRefusals.MissingCode
            => StatusCodes.Status402PaymentRequired,
        _ => StatusCodes.Status400BadRequest,
    };
}
