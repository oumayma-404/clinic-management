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
/// Connectivity signal. Anonymous so the frontend can poll it before login and from any LAN client; returns
/// only a non-sensitive tri-state.
///
/// <para>⚠️ <b>It used to 404 in a hosted deployment, on the reasoning that "the frontend never polls there".
/// The frontend polls everywhere</b> — that is AC-62, and it is the only thing that answers « is the clinic's
/// server reachable at all ». So the 404 was not a gate, it was 240 spurious errors an hour per open tab in
/// every hosted browser console, which is what support reads when a clinic reports a bug. The route now
/// answers 200 with <c>InternetReachable = null</c>: the endpoint exists, and it has no egress reading to
/// give. The client's state machine is unchanged — a body whose <c>internetReachable</c> is not a boolean
/// already resolves to « signal absent », AC-63's third row.</para>
///
/// <para>Not <c>/health</c> instead, either: that route deliberately carries no <c>lb_try_duration</c> in
/// <c>deploy/Caddyfile</c>, so during a rolling deploy it reports the API down while <c>/api/*</c> requests are
/// being held and served — a false « serveur injoignable » on every ship.</para>
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
        // The egress READING is the part that exists only for a box the clinic hosts itself — the same
        // capability as the trust page. The route itself is answered everywhere, because reachability is not
        // a capability, it is the question the poll is really asking.
        if (!_deployment.ExposesTrustEndpoints)
        {
            return Ok(new ConnectivityStatusDto { InternetReachable = null });
        }

        var result = await _mediator.Send(new GetConnectivityStatusQuery());
        var dto = result.IsSuccess && result.Value is not null
            ? result.Value
            : new ConnectivityStatusDto { InternetReachable = false };

        return Ok(dto);
    }
}
