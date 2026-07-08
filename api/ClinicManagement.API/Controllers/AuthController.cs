using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using MediatR;
using ClinicManagement.Application.Features.Auth.Commands;
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
}
