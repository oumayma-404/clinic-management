using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Backup.Commands;
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

    /// <summary>
    /// A clinic PC's report that it copied the camera originals it keeps to the archive disk — posted by the page on
    /// the Windows app's behalf. Here rather than beside <c>vault-copy</c> because <c>BackupController</c> is
    /// <c>AdminOnly</c> at the class level and policies add up: the person signed in on the camera's PC is usually
    /// reception. <c>covered</c> says whether the copy reached everything on record, for the vault report's reason.
    /// </summary>
    [HttpPost("originals-copy")]
    [AllowsWithoutSubscription(
        "Reporting that the practice holds a second copy of its own photos is not recording new work, and a lapsed "
        + "cabinet must not be the one that stops being told its originals sit on one disk.")]
    public async Task<IActionResult> ReportOriginalsCopy(
        [FromBody] ReportPhotoOriginalsCopyCommand command,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return result.IsSuccess ? Ok(new { covered = result.Value }) : HandleFailure(result);
    }
}
