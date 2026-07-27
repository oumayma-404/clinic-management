using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using MediatR;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.API.Models;
using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.API.Startup;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Local (offline) authentication endpoints. Active in Local mode; the login endpoint has no
/// effect in Cloud mode (Auth0 owns login there).
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ApiControllerBase
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
    // Per-client-address limit: this is the brute-force surface (US-4 / AC-4.1).
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
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
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] SetupRequest request)
    {
        if (!LocalAuthConfig.IsLocalMode(_configuration))
        {
            return NotFound();
        }

        if (!ClinicManagement.Infrastructure.LocalRequest.IsLoopback(HttpContext))
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
            City = request.City,
            Role = "admin",
            DoctorInfo = request.DoctorInfo, // when set, the admin is also the practitioner (Doctor is created + linked)
            GenerateCode = true,
            WorkingHoursJson = request.WorkingHoursJson
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Local-mode staff self-registration: join a clinic by code with email+password.
    /// Reachable from any LAN client (the clinic code is the gate — not localhost). Does not
    /// exist in Cloud mode. Admin is never self-assignable (enforced in the handler).
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.AnonymousAuthPolicy)]
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
            return HandleFailure(result);
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
            return HandleFailure(result);
        }

        return Ok(result);
    }
}
