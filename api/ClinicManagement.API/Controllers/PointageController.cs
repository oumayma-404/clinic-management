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
/// The borne (<c>pointage-borne</c>): a reception device nobody is signed in on. It proves what it is with an
/// <c>HttpOnly</c> cookie an admin set from « Faire de cet appareil une borne »; each person proves who she is with
/// her schéma. The class policy keeps any future action here closed; each anonymous one is on the reviewed list.
/// </summary>
[ApiController]
[Route("api/pointage")]
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
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
            });
        }

        BorneCookie.Write(Response, Request.IsHttps, secret!);
        return Ok(result.Value.Done);
    }

    public sealed record PunchRequest(string? UserId, List<int>? Pattern, string? Expected = null);

    private static int StatusFor(string? code) => code switch
    {
        AttendanceRules.TerminalUnknownCode => StatusCodes.Status401Unauthorized,
        AttendanceRules.SchemaLockedCode => StatusCodes.Status423Locked,
        AttendanceRules.SchemaMissingCode or AttendanceRules.OnLeaveCode or AttendanceRules.PunchStateChangedCode
            => StatusCodes.Status409Conflict,
        AttendanceRules.ModuleOffCode or AttendanceRules.PersonNotTrackedCode => StatusCodes.Status403Forbidden,
        SubscriptionRefusals.RequiredCode or SubscriptionRefusals.SuspendedCode or SubscriptionRefusals.MissingCode
            => StatusCodes.Status402PaymentRequired,
        _ => StatusCodes.Status400BadRequest,
    };
}
