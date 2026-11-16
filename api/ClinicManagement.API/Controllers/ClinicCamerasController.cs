using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Cameras;
using ClinicManagement.Application.Features.Cameras.Commands;
using ClinicManagement.Application.Features.Cameras.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// The cabinet's paired cameras (<c>features/camera-photo-import</c>). Pairing, renaming and recording an import are
/// reception's work as much as the dentist's, so the class is <c>AnyClinicRole</c>; forgetting a camera is
/// <c>AdminOrDoctor</c> — « enregistrer oui, effacer non ».
/// </summary>
[ApiController]
[Route("api/clinic-cameras")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class ClinicCamerasController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public ClinicCamerasController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClinicCameraDto>>> List(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ListClinicCamerasQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    /// <summary>Pairs a camera, or returns the one already paired under this fingerprint.</summary>
    [HttpPost]
    public async Task<ActionResult<ClinicCameraDto>> Register(
        [FromBody] RegisterClinicCameraCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    [HttpPut("{cameraId}")]
    public async Task<ActionResult<ClinicCameraDto>> Update(
        Guid cameraId,
        [FromBody] UpdateClinicCameraCommand command,
        CancellationToken cancellationToken = default)
    {
        command.CameraId = cameraId;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : HandleFailure(result, result.Code == Application.Features.Files.CameraImportRefusals.CameraNotFoundCode
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest);
    }

    /// <summary>What « Ranger » tells the registry: the latest shot filed and any clock correction.</summary>
    [HttpPost("{cameraId}/imports")]
    public async Task<ActionResult<ClinicCameraDto>> RecordImport(
        Guid cameraId,
        [FromBody] RecordCameraImportCommand command,
        CancellationToken cancellationToken = default)
    {
        command.CameraId = cameraId;
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : HandleFailure(result, result.Code == Application.Features.Files.CameraImportRefusals.CameraNotFoundCode
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest);
    }

    [HttpDelete("{cameraId}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<IActionResult> Forget(Guid cameraId, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ForgetClinicCameraCommand { CameraId = cameraId }, cancellationToken);
        return result.IsSuccess ? NoContent() : HandleFailure(result);
    }
}
