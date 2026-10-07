using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// « Paramètres → PC de secours » on the PC de secours itself (<c>clinic-pc-copy</c> AC-8.1): what this PC is now —
/// still following its cabinet, or retired since a given day. Admins only, like the card; 404 anywhere else.
/// </summary>
[ApiController]
[Route("api/relay/local")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class RelayLocalController : ApiControllerBase
{
    private readonly DeploymentProfile _deployment;
    private readonly IRelayLocalStatus _status;

    public RelayLocalController(DeploymentProfile deployment, IRelayLocalStatus status)
    {
        _deployment = deployment;
        _status = status;
    }

    [HttpGet]
    public ActionResult<RelayLocalStatusDto> Get()
    {
        if (!_deployment.MirrorsCloudClinic)
        {
            return NotFound();
        }

        return Ok(new RelayLocalStatusDto(
            _status.IsRetired,
            _status.RetiredAtUtc,
            RelayLabels.Local(_status.IsRetired, _status.RetiredAtUtc)));
    }
}
