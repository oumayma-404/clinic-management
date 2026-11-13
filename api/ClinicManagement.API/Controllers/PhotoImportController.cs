using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.PhotoImport.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// The two reads behind « Importer les photos » (<c>features/camera-photo-import</c>). Neither writes: the photos go
/// in through the ordinary patient-file doors, which is what keeps one upload policy, one quota and one audit trail.
///
/// <para>⚠️ <b><c>known</c> is a POST that reads</b>: a card's keys are a list of up to two thousand 64-character
/// hashes, which no query string carries. It persists nothing.</para>
/// </summary>
[ApiController]
[Route("api/photo-import")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class PhotoImportController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PhotoImportController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The visits, fiche times and doctors of a date range, in clinic wall-clock.</summary>
    [HttpGet("context")]
    public async Task<ActionResult<PhotoImportContextDto>> GetContext(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] Guid? doctorId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetPhotoImportContextQuery { FromDay = from, ToDay = to, DoctorId = doctorId }, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    /// <summary>
    /// Which of these shots this cabinet has already filed. Deliberately NOT exempt from the subscription gate: on an
    /// expired cabinet nothing could be filed after it anyway, and refusing here says so before a photo is read.
    /// </summary>
    [HttpPost("known")]
    public async Task<ActionResult<IReadOnlyList<ImportedCaptureDto>>> GetKnown(
        [FromBody] Models.KnownCapturesRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetImportedCaptureKeysQuery { CaptureKeys = request.CaptureKeys }, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }
}
