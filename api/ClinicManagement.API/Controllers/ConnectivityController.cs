using Microsoft.AspNetCore.Authorization;
using ClinicManagement.Application.Common.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MediatR;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Connectivity.Queries;
using ClinicManagement.Infrastructure.Deployment;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// Local (offline-LAN) connectivity signal. Anonymous so the frontend can poll it before login and
/// from any LAN client; returns only a non-sensitive boolean. <b>404s in a hosted deployment</b> — a server in a
/// datacentre has no offline story to tell, and the frontend never polls there.
/// The anonymous exception is deliberate (like <c>GET /api/auth/mode</c>) and flagged for the Phase 4
/// "auth on all endpoints" release-gate review (R-6).
/// </summary>
[ApiController]
[Route("api/connectivity")]
// The single action below is deliberately [AllowAnonymous] (the frontend polls it before login). The class
// policy exists so a future action added here is covered rather than silently anonymous.
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class ConnectivityController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;

    private readonly DeploymentProfile _deployment;

    public ConnectivityController(IMediator mediator, IConfiguration configuration, DeploymentProfile deployment)
    {
        _mediator = mediator;
        _configuration = configuration;
        _deployment = deployment;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        // Same capability as the trust page: both exist only for a box the clinic hosts itself.
        if (!_deployment.ExposesTrustEndpoints)
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
