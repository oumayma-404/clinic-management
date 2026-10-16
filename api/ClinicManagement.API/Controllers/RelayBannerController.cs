using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Relay.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// The cut's strip on every screen (<c>clinic-pc-copy</c> AC-3.4, AC-4.1, D20) — read by everybody in the cabinet, on
/// the cloud and on the PC de secours alike. 204 when nothing is under way, which is almost always.
/// </summary>
[ApiController]
[Route("api/relay/banner")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class RelayBannerController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public RelayBannerController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<RelayBannerDto>> Get(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetRelayBannerQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return HandleFailure(result);
        }

        return result.Value is { } banner ? Ok(banner) : NoContent();
    }
}
