using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Infrastructure.Deployment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers.Platform;

/// <summary>
/// « Sauvegarde hors serveur » — whether the host's two off-site copies are healthy (<c>server-loss-recovery</c>
/// Part 3), for the console's status strip.
///
/// <para><b>Its own controller, read-only by construction</b>: there is no action here that runs, retries or
/// silences a backup. Acting on the server is an operator's deliberate step, never a button on a page.</para>
///
/// <para>⚠️ <b>404 where <see cref="DeploymentProfile.MonitorsSidecarBackups"/> is false</b>, answered before the
/// mediator — absent rather than present-and-empty, so a deployment with no sidecars is never told its backups are
/// « en retard ». The console listener itself only exists where the console is served.</para>
/// </summary>
[ApiController]
[Route("api/platform")]
[Authorize(Policy = AuthorizationPolicies.PlatformConsole)]
public class PlatformBackupHealthController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly DeploymentProfile _deployment;

    public PlatformBackupHealthController(IMediator mediator, DeploymentProfile deployment)
    {
        _mediator = mediator;
        _deployment = deployment;
    }

    /// <summary>The nightly run and the WAL stream, each judged, and the worse of the two.</summary>
    [HttpGet("backup-health")]
    public async Task<ActionResult<PlatformBackupHealthDto>> GetBackupHealth(CancellationToken cancellationToken)
    {
        if (!_deployment.MonitorsSidecarBackups)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetPlatformBackupHealthQuery(), cancellationToken);
        return result.IsFailure ? HandleFailure(result) : Ok(result.Value);
    }
}
