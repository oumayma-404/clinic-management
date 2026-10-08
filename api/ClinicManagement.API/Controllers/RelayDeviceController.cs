using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Application.Features.Relay.Queries;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Deployment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// What the cabinet's Windows and Android apps tell the cloud about its PC de secours (<c>clinic-pc-copy</c> AC-6.2): asked
/// every half-minute, and — while the cloud is locked for a PC that said nothing — whether the device reached it. Any
/// clinic role: a reception tablet is exactly the device that notices. A browser never calls it (no bridge to probe with).
/// </summary>
[ApiController]
[Route("api/relay/devices")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
[AllowedWhileCloudFenced("The cabinet's devices saying they reach the cloud but not a silent PC is how its lock ends (AC-6.2).")]
public class RelayDeviceController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;
    private readonly TrustedProxies _trustedProxies;

    public RelayDeviceController(IMediator mediator, DeploymentProfile deployment, TrustedProxies trustedProxies)
    {
        _mediator = mediator;
        _deployment = deployment;
        _trustedProxies = trustedProxies;
    }

    [HttpGet("target")]
    public async Task<ActionResult<RelayDeviceTargetDto>> Target(CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetRelayDeviceTargetQuery(CallerAddress()), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    [OnlineOnly("Only the cloud is unlocked by the cabinet's devices; a PC in charge has nothing to learn from them.")]
    [HttpPost("report")]
    [AllowsWithoutSubscription("A device saying whether it reaches the PC de secours records no work of the cabinet's.")]
    public async Task<ActionResult<RelayDeviceReportDto>> Report(
        [FromBody] RelayDeviceReportRequest request, CancellationToken cancellationToken)
    {
        if (!_deployment.PublishesChangeFeed)
        {
            return NotFound();
        }

        var result = await _mediator.Send(
            new ReportRelayDeviceCommand(request.ReachesPc, request.Gateways, CallerAddress()), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }

    private string CallerAddress() => ClientIp.Resolve(HttpContext, _trustedProxies);
}
