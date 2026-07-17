using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Connectivity.Queries;
using ClinicManagement.Infrastructure.Auth;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Local (offline-LAN) connectivity signal. Anonymous so the frontend can poll it before login and
/// from any LAN client; returns only a non-sensitive boolean. <b>404s in Cloud mode</b> (mirrors the
/// Local-only auth endpoints) — Cloud has no offline story and the frontend never polls there.
/// The anonymous exception is deliberate (like <c>GET /api/auth/mode</c>) and flagged for the Phase 4
/// "auth on all endpoints" release-gate review (R-6).
/// </summary>
[ApiController]
[Route("api/connectivity")]
public class ConnectivityController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    public ConnectivityController(IMediator mediator, IConfiguration configuration)
    {
        _mediator = mediator;
        _configuration = configuration;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!LocalAuthConfig.IsLocalMode(_configuration))
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetConnectivityStatusQuery());
        var dto = result.IsSuccess && result.Value is not null
            ? result.Value
            : new ConnectivityStatusDto { InternetReachable = false };

        return Ok(dto);
    }
}
