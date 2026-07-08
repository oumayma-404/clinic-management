using Microsoft.AspNetCore.Mvc;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Users.Queries;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.API.Models;
using Microsoft.AspNetCore.Authorization;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all users for the current admin's clinic, with account status (AC-5.1).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClinicUserDto>>> GetUsers()
    {
        var query = new ListUsersQuery();
        var result = await _mediator.Send(query);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Reset a user's password to a temporary one and force a change at next login (AC-5.2).
    /// The temporary password is returned once for the admin to relay.
    /// </summary>
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<ResetPasswordResultDto>> ResetPassword(string id)
    {
        var command = new ResetUserPasswordCommand { TargetUserId = id };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Deactivate or reactivate a user (AC-5.3). Historical records are retained.
    /// </summary>
    [HttpPut("{id}/status")]
    public async Task<ActionResult<ClinicUserDto>> SetStatus(string id, [FromBody] SetUserStatusRequest request)
    {
        var command = new SetUserActiveCommand { TargetUserId = id, IsActive = request.IsActive };
        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }
}
