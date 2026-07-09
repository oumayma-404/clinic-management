using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Backup.Commands;
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
public class BackupController : ControllerBase
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
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }
}
