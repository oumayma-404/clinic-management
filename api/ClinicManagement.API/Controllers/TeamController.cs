using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Application.Features.Team.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Secretary congés (<c>conges-secretaires</c>): a secretary asks, an admin decides. Thin pass-through; who may do
/// what is re-checked in every handler.
/// </summary>
[ApiController]
[Route("api/team")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class TeamController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public TeamController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The cabinet's requests: <c>status=pending</c> (to decide), <c>decided</c>, or all.</summary>
    [HttpGet("leave-requests")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<StaffLeaveRequestsPageDto>> ListLeaveRequests(
        [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ListStaffLeaveRequestsQuery { Status = status, Page = page, PageSize = pageSize }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>Each active secretary's balance for the year and next accepted congé.</summary>
    [HttpGet("overview")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<TeamLeaveOverviewDto>> Overview(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTeamLeaveOverviewQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    [HttpGet("leave-requests/pending-count")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<PendingStaffLeaveCountDto>> PendingCount(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPendingStaffLeaveCountQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    // Open to every role: the handler answers its owner or an admin, and « introuvable » to anyone else.
    [HttpGet("leave-requests/{id:guid}")]
    public async Task<ActionResult<StaffLeaveRequestDto>> GetLeaveRequest(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetStaffLeaveRequestQuery { Id = id }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    [HttpPost("leave-requests/{id:guid}/decision")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<StaffLeaveRequestDto>> Decide(
        Guid id, [FromBody] DecideStaffLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    // Open to every role: the owner withdraws her own; the handler lets an admin withdraw anyone's.
    [HttpPost("leave-requests/{id:guid}/cancel")]
    public async Task<ActionResult<StaffLeaveRequestDto>> Cancel(
        Guid id, [FromBody] CancelStaffLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    [HttpGet("me/leave")]
    public async Task<ActionResult<MyStaffLeaveDto>> MyLeave([FromQuery] int? year, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyStaffLeaveQuery { Year = year }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    // Open to every role so the refusal is the handler's French sentence (secretary accounts only).
    [HttpPost("me/leave-requests")]
    public async Task<ActionResult<StaffLeaveRequestDto>> Submit(
        [FromBody] SubmitStaffLeaveRequestCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>The day count and the balance after, before a request is sent.</summary>
    [HttpGet("leave-preview")]
    public async Task<ActionResult<StaffLeavePreviewDto>> Preview(
        [FromQuery] string? userId,
        [FromQuery] string? type,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        [FromQuery] bool halfDay,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PreviewStaffLeaveQuery
        {
            UserId = userId,
            Type = type,
            StartDate = startDate,
            EndDate = endDate,
            HalfDay = halfDay,
        }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    [HttpGet("users/{userId}/file")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<StaffFileDto>> StaffFile(
        string userId, [FromQuery] int? year, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetStaffFileQuery { UserId = userId, Year = year }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    [HttpPost("users/{userId}/absences")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<StaffLeaveRequestDto>> RecordAbsence(
        string userId, [FromBody] RecordStaffAbsenceCommand command, CancellationToken cancellationToken)
    {
        command.UserId = userId;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    [HttpPut("users/{userId}/leave-allowance")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<StaffLeaveBalanceDto>> SetAllowance(
        string userId, [FromBody] SetLeaveAllowanceCommand command, CancellationToken cancellationToken)
    {
        command.UserId = userId;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    // « Introuvable » is a 404; every other congé refusal is a 400 carrying its French sentence.
    private static int StatusFor(Result result) =>
        result.Code == StaffLeaveRules.NotFoundCode ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
}
