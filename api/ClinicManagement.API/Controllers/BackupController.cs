using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Backup.Commands;
using ClinicManagement.Application.Features.Backup.Queries;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.API.Models;
using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Admin-only one-click "Backup now" (US-8 / FR-G). Thin MediatR pass-through: a success returns the
/// destination path + size (AC-8.2), a failure returns the clear operator-facing reason as a 400 —
/// never a silent success (AC-8.2 / AC-8.3). The backup mechanism is Local-oriented (bundled pg_dump +
/// local file storage); the frontend only surfaces it in Local mode for admins.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class BackupController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public BackupController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Run a backup now — dumps the database and copies the file-storage folder to a timestamped
    /// subfolder of the destination (AC-8.1).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BackupResultDto>> BackupNow([FromBody] BackupRequest request)
    {
        var command = new BackupNowCommand { DestinationFolder = request.DestinationFolder };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// « Historique des sauvegardes » (L4d) — the clinic's recorded attempts, newest first, plus the last
    /// successful moment, the resolved default destination and the schedule.
    ///
    /// <para>The read that turns backup from a habit into a guarantee: before it, the result of a backup lived in
    /// a React <c>useState</c> and « quand la dernière sauvegarde a-t-elle réussi ? » had no answer anywhere in
    /// the product.</para>
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<BackupHistoryDto>> History([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var result = await _mediator.Send(new GetBackupHistoryQuery { Page = page, PageSize = pageSize });

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// The unattended-backup schedule (L4a): on/off, the clinic-local hour, how many copies to keep, and the
    /// staleness threshold. The caller the four new columns ship with — a setting with no writer is the
    /// <c>SetStockExpiryLeadDays</c> failure the spec names.
    /// </summary>
    [HttpPut("schedule")]
    public async Task<ActionResult<BackupScheduleDto>> SetSchedule([FromBody] SetBackupScheduleCommand command)
    {
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }
}
