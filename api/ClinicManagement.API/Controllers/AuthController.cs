using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using MediatR;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.API.Models;
using ClinicManagement.Infrastructure.Auth;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Local (offline) authentication endpoints. Active in Local mode; the login endpoint has no
/// effect in Cloud mode (Auth0 owns login there).
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public AuthController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }

    /// <summary>
    /// Public: reports the configured auth mode so the frontend can render the right login UI.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("mode")]
    public IActionResult GetMode()
    {
        var mode = LocalAuthConfig.IsLocalMode(_configuration)
            ? LocalAuthConfig.LocalMode
            : LocalAuthConfig.CloudMode;
        return Ok(new { mode });
    }

    /// <summary>
    /// Local-mode login: email + password → signed JWT. Returns 401 on any failure.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Local-mode only — Cloud login is owned by Auth0 (mirrors Setup/Register).
        if (!LocalAuthConfig.IsLocalMode(_configuration))
        {
            return NotFound();
        }

        var command = new LoginCommand
        {
            Email = request.Email,
            Password = request.Password
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Local-mode first-run setup: creates the clinic + first admin (email+password).
    /// Reachable only from the server machine (localhost) and only until the first admin
    /// exists — AC-1.2a. Does not exist in Cloud mode.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupRequest request)
    {
        if (!LocalAuthConfig.IsLocalMode(_configuration))
        {
            return NotFound();
        }

        if (!IsLocalRequest(HttpContext))
        {
            return StatusCode((int)HttpStatusCode.Forbidden, new { error = "First-run setup is only available on the server machine." });
        }

        var command = new CreateClinicCommand
        {
            Name = request.ClinicName,
            Email = request.Email,
            Password = request.Password,
            FullName = request.FullName,
            Phone = request.Phone,
            Address = request.Address,
            Role = "admin",
            GenerateCode = true
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Local-mode staff self-registration: join a clinic by code with email+password.
    /// Reachable from any LAN client (the clinic code is the gate — not localhost). Does not
    /// exist in Cloud mode. Admin is never self-assignable (enforced in the handler).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!LocalAuthConfig.IsLocalMode(_configuration))
        {
            return NotFound();
        }

        var command = new JoinClinicCommand
        {
            Code = request.Code,
            Role = request.Role,
            DoctorInfo = request.DoctorInfo,
            Email = request.Email,
            Password = request.Password,
            FullName = request.FullName,
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Authenticated: the current user sets a new password (verifying the current one). Used
    /// for the forced change after an admin reset and for voluntary changes (AC-5.2).
    /// </summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var command = new ChangePasswordCommand
        {
            CurrentPassword = request.CurrentPassword,
            NewPassword = request.NewPassword
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>True when the request originates from the server machine itself (loopback).</summary>
    private static bool IsLocalRequest(HttpContext context)
    {
        var connection = context.Connection;
        var remoteIp = connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return true; // in-process / no remote info
        }
        if (connection.LocalIpAddress is not null)
        {
            return remoteIp.Equals(connection.LocalIpAddress) || IPAddress.IsLoopback(remoteIp);
        }
        return IPAddress.IsLoopback(remoteIp);
    }
}
