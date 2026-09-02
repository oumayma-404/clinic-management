using ClinicManagement.Infrastructure.Auth;
using ClinicManagement.API.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Application.Features.Files.Queries;
using System.Security.Claims;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/patients/{patientId}/files")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class PatientFilesController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly IFileResidencyPolicy _residencyPolicy;
    private readonly ILogger<PatientFilesController> _logger;

    public PatientFilesController(
        IMediator mediator,
        IFileResidencyPolicy residencyPolicy,
        ILogger<PatientFilesController> logger)
    {
        _mediator = mediator;
        _residencyPolicy = residencyPolicy;
        _logger = logger;
    }

    [HttpPost("folders/initialize-defaults")]
    [AllowsWithoutSubscription("FR-3 — fired on the first visit to the Files tab; a READ would fail without it (AC-4.1).")]
    public async Task<ActionResult<IEnumerable<Application.DTOs.PatientFolderDto>>> InitializeDefaultFolders(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var command = new InitializeDefaultFoldersCommand
        {
            PatientId = patientId
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpGet("folders")]
    public async Task<ActionResult<IEnumerable<Application.DTOs.PatientFolderDto>>> GetFolders(
        Guid patientId,
        [FromQuery] Guid? parentFolderId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPatientFoldersQuery
        {
            PatientId = patientId,
            ParentFolderId = parentFolderId
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<Application.DTOs.PatientFileDto>>> GetFiles(
        Guid patientId,
        [FromQuery] Guid? folderId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPatientFilesQuery
        {
            PatientId = patientId,
            FolderId = folderId,
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPost("folders")]
    public async Task<ActionResult<Application.DTOs.PatientFolderDto>> CreateFolder(
        Guid patientId,
        [FromBody] CreatePatientFolderCommand command,
        CancellationToken cancellationToken = default)
    {
        command.PatientId = patientId;
        var uploadedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetFolders), new { patientId }, result.Value);
    }

    // AC-3.6: the ceiling is the catalog's, per action — ASP.NET's default 30 MB body limit would otherwise be
    // the real one, and a 150 MB CBCT study would die on a framework 413 the app never sees and cannot explain.
    [HttpPost("upload")]
    [RequestSizeLimit(FileTypeCatalog.MaxBytesAcrossCatalog)]
    [RequestFormLimits(MultipartBodyLengthLimit = FileTypeCatalog.MaxBytesAcrossCatalog)]
    public async Task<ActionResult<Application.DTOs.PatientFileDto>> UploadFile(
        Guid patientId,
        [FromForm] Models.UploadFileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return Failure("Le fichier est requis.");
        }

        var uploadedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        var command = new UploadPatientFileCommand
        {
            PatientId = patientId,
            FolderId = request.FolderId,
            FileName = request.File.FileName,
            FileSize = request.File.Length,
            FileStream = request.File.OpenReadStream(),
            Description = request.Description,
            UploadedBy = uploadedBy,
            PreviewStream = request.Preview?.OpenReadStream(),
            PreviewFileName = request.Preview?.FileName,
            PreviewSize = request.Preview?.Length ?? 0
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetFiles), new { patientId }, result.Value);
    }

    // The coffre door. ⚠️ No [RequestSizeLimit] sized from the original — the original is not here. What crosses
    // the wire is a description plus, at most, a 4 Mo preview, which is why a 25 Go study can be recorded at all.
    // ⚠️ Sized at TWICE the preview cap, deliberately. The handler's contract is that an oversized preview is
    // **dropped while the row still registers** — but a body limit at the cap itself is enforced by Kestrel before
    // model binding, so an over-large picture would 413 the whole request and lose the registration, which is the
    // opposite of the rule. The headroom is what lets the handler be the one to decide.
    [HttpPost("vault")]
    [RequestSizeLimit(2 * FileTypeCatalog.PreviewBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2 * FileTypeCatalog.PreviewBytes)]
    public async Task<ActionResult<Application.DTOs.PatientFileDto>> RegisterVaultFile(
        Guid patientId,
        [FromForm] Models.RegisterVaultFileRequest request,
        CancellationToken cancellationToken = default)
    {
        // Absent, not refusing: where the clinic's own machine is the object store there is no coffre to file
        // anything in, so the route does not exist rather than answering a refusal nobody can act on (AC-7).
        if (!_residencyPolicy.VaultAvailable)
        {
            return NotFound();
        }

        var uploadedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        var command = new RegisterVaultFileCommand
        {
            PatientId = patientId,
            FileId = request.FileId,
            FolderId = request.FolderId,
            FileName = request.FileName,
            FileSize = request.FileSize,
            ContentHash = request.ContentHash,
            Description = request.Description,
            UploadedBy = uploadedBy,
            PreviewStream = request.Preview?.OpenReadStream(),
            PreviewFileName = request.Preview?.FileName,
            PreviewSize = request.Preview?.Length ?? 0
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetFiles), new { patientId }, result.Value);
    }

    /// <summary>
    /// The stand-in image for a coffre original. Served <b>inline</b> — unlike a download, this is a derived
    /// thumbnail the app renders itself, and it is a raster the catalog validated on the way in.
    /// </summary>
    [HttpGet("{fileId}/preview")]
    public async Task<IActionResult> DownloadPreview(
        Guid patientId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var query = new DownloadPatientFilePreviewQuery
        {
            PatientId = patientId,
            FileId = fileId
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        var previewDto = result.Value!;

        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return File(previewDto.FileStream, previewDto.ContentType);
    }

    [HttpGet("{fileId}/download")]
    // ⚠️ Reachable by the unattended workstation's scoped token, because the file mirror fetches every file
    // through here one at a time — without this line the mirror stops copying and the cabinet quietly loses the
    // local copy of its imaging.
    //
    // It widens the scope by nothing: the same token can pull GET /api/backup/archive, which carries these very
    // files in one download. What the scope still refuses is everything else — the patient records, the
    // ledgers, the exports, user management.
    [AcceptsScopedToken(LocalAuthScopes.ClinicArchive)]
    public async Task<IActionResult> DownloadFile(
        Guid patientId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var query = new DownloadPatientFileQuery
        {
            PatientId = patientId,
            FileId = fileId
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        var fileDto = result.Value!;

        // AC-11.6: serve the STORED, validated type and make the response inert. `nosniff` stops the browser
        // second-guessing the type, and the attachment disposition (which the fileDownloadName overload sets)
        // means nothing renders in the app's own origin. This also covers files stored before upload
        // validation existed, whose type was never checked (AC-11.9) — they download, but cannot execute.
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        // ⚠️ Set by hand because the stream is no longer seekable. ASP.NET reads `Content-Length` off a seekable
        // stream's own length, and buffering the whole object in memory used to supply it as a side effect — so
        // without this line a browser downloading a study reports « unknown size » and shows no progress at all,
        // which on a clinic's uplink is exactly when somebody is watching it.
        if (fileDto.Length is > 0)
        {
            Response.ContentLength = fileDto.Length;
        }

        return File(fileDto.FileStream, fileDto.ContentType, fileDto.FileName);
    }

    // AC-4.4 — record yes, erase no: renaming, describing and moving are recording, so they stay on the class
    // policy beside upload. Only the two deletes below are tightened.
    [HttpPut("{fileId}")]
    public async Task<ActionResult<Application.DTOs.PatientFileDto>> UpdateFile(
        Guid patientId,
        Guid fileId,
        [FromBody] UpdatePatientFileCommand command,
        CancellationToken cancellationToken = default)
    {
        command.PatientId = patientId;
        command.FileId = fileId;

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPut("folders/{folderId}")]
    public async Task<ActionResult<Application.DTOs.PatientFolderDto>> RenameFolder(
        Guid patientId,
        Guid folderId,
        [FromBody] RenamePatientFolderCommand command,
        CancellationToken cancellationToken = default)
    {
        command.PatientId = patientId;
        command.FolderId = folderId;

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    // Reception scans documents *in* — uploading, listing and downloading are the front desk's job, which is
    // why the class policy is open. Removing a scanned document is not: nothing on any screen afterwards says
    // it was ever there.
    [HttpDelete("{fileId}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<IActionResult> DeleteFile(
        Guid patientId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var command = new DeletePatientFileCommand
        {
            PatientId = patientId,
            FileId = fileId
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }

    [HttpDelete("folders/{folderId}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
    public async Task<IActionResult> DeleteFolder(
        Guid patientId,
        Guid folderId,
        CancellationToken cancellationToken = default)
    {
        var command = new DeletePatientFolderCommand
        {
            PatientId = patientId,
            FolderId = folderId
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }
}

