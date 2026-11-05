using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
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
/// Secretary congés and pointage (<c>conges-secretaires</c>, <c>pointage-borne</c>): a secretary asks, an admin
/// decides; the borne records, an admin corrects. Thin pass-through; who may do what is re-checked in every handler.
/// The borne's own anonymous endpoints are <see cref="PointageController"/>.
/// </summary>
[ApiController]
[Route("api/team")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class TeamController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IClinicContext _clinicContext;
    private readonly IStepUpConfirmations _stepUp;

    public TeamController(IMediator mediator, IClinicContext clinicContext, IStepUpConfirmations stepUp)
    {
        _mediator = mediator;
        _clinicContext = clinicContext;
        _stepUp = stepUp;
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

    /// <summary>« Équipe → Pointage » in Paramètres: the bornes and who pointe.</summary>
    [HttpGet("attendance-settings")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<AttendanceSettingsDto>> AttendanceSettings(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAttendanceSettingsQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>
    /// « Faire de cet appareil une borne ». Spends a step-up confirmation before the mediator, then puts the borne's
    /// secret in an <c>HttpOnly</c> cookie — the body carries the borne, never the secret.
    /// </summary>
    [HttpPost("terminals")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<AttendanceTerminalDto>> RegisterTerminal(
        [FromBody] RegisterTerminalRequest request, CancellationToken cancellationToken)
    {
        var callerId = _clinicContext.GetUserId();
        if (string.IsNullOrWhiteSpace(callerId)
            || !_stepUp.Consume(callerId, AttendanceRules.RegisterTerminalStepUpAction, request.StepUpToken ?? string.Empty))
        {
            return Failure(
                "Cette action demande une confirmation récente de votre identité. Veuillez réessayer.",
                StatusCodes.Status403Forbidden);
        }

        var result = await _mediator.Send(new RegisterAttendanceTerminalCommand
        {
            Name = request.Name,
            ReplacingTerminalId = request.ReplacingTerminalId,
        }, cancellationToken);
        if (result.IsFailure)
            return HandleFailure(result, StatusFor(result));

        BorneCookie.Write(Response, Request.IsHttps, result.Value!.Secret);
        return Ok(result.Value.Terminal);
    }

    /// <summary>« Retirer »: takes effect at the device's next punch.</summary>
    [HttpDelete("terminals/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<AttendanceTerminalDto>> RevokeTerminal(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RevokeAttendanceTerminalCommand { Id = id }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>« Schéma de pointage » in Mon profil: her own, drawn twice on the client.</summary>
    [HttpPut("me/schema")]
    public async Task<ActionResult<AttendancePersonSettingDto>> SetMySchema(
        [FromBody] SetMyAttendanceSchemaCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>« Réinitialiser le schéma »: clears it and lifts a lock.</summary>
    [HttpDelete("users/{userId}/schema")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<AttendancePersonSettingDto>> ResetSchema(string userId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ResetAttendanceSchemaCommand { UserId = userId }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>« Personnes qui pointent »: <c>tracked</c> true/false, or null to follow the role.</summary>
    [HttpPut("users/{userId}/attendance-profile")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<AttendancePersonSettingDto>> SetAttendanceProfile(
        string userId, [FromBody] SetAttendanceProfileCommand command, CancellationToken cancellationToken)
    {
        command.UserId = userId;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>« Présences »: the month sheet, for everyone or one person.</summary>
    [HttpGet("attendance")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<AttendanceSheetDto>> AttendanceSheet(
        [FromQuery] string? month, [FromQuery] string? userId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAttendanceSheetQuery { Month = month, UserId = userId }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    [HttpGet("attendance/export")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    [RefusedInDemo("Hands the cabinet's data out of the demo as a CSV.")]
    public async Task<IActionResult> ExportAttendance([FromQuery] string? month, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ExportAttendanceQuery { Month = month }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Csv(result.Value!, "presences");
    }

    /// <summary>« Mes présences »: her own month, read-only.</summary>
    [HttpGet("me/attendance")]
    public async Task<ActionResult<MyAttendanceDto>> MyAttendance([FromQuery] string? month, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyAttendanceQuery { Month = month }, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>« Corriger »: the hours that count, with a motif; the borne's own hours stay on the line.</summary>
    [HttpPost("attendance/{id:guid}/correction")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<AttendanceEntryDto>> CorrectAttendance(
        Guid id, [FromBody] CorrectAttendanceEntryCommand command, CancellationToken cancellationToken)
    {
        command.Id = id;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>« Ajouter un pointage » for a day with none, with a motif.</summary>
    [HttpPost("attendance")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<AttendanceEntryDto>> RecordAttendance(
        [FromBody] RecordAttendanceEntryCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    /// <summary>« Équipe aujourd'hui » on the dashboard.</summary>
    [HttpGet("today")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<ActionResult<TeamTodayDto>> Today(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTeamTodayQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result, StatusFor(result)) : Ok(result.Value);
    }

    public sealed record RegisterTerminalRequest(string? Name, string? StepUpToken, Guid? ReplacingTerminalId = null);

    // « Introuvable » is a 404, the pointage switched off a 403; every other refusal is a 400 carrying its sentence.
    private static int StatusFor(Result result) => result.Code switch
    {
        StaffLeaveRules.NotFoundCode or AttendanceRules.NotFoundCode => StatusCodes.Status404NotFound,
        AttendanceRules.ModuleOffCode => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status400BadRequest,
    };
}
