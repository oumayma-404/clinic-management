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

    // ── Resumable upload (large-file-transfer Part 2) ──────────────────────────────────────────

    /// <summary>
    /// Opens an upload. ⚠️ <b>No <c>[RequestSizeLimit]</c> sized from the file</b> — the file is not here. What
    /// crosses the wire is a name and a length, which is precisely what lets this refuse a 200 Mo upload of a
    /// format the deployment does not take before the clinic spends a minute sending it.
    /// </summary>
    [HttpPost("uploads")]
    public async Task<ActionResult<Application.Features.Files.Commands.FileUploadSessionDto>> StartUpload(
        Guid patientId,
        [FromBody] Models.StartFileUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new Application.Features.Files.Commands.StartFileUploadCommand
        {
            PatientId = patientId,
            FileName = request.FileName,
            FileSize = request.FileSize,
            FolderId = request.FolderId,
            Description = request.Description,
            UploadedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
        };

        var result = await _mediator.Send(command, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    /// <summary>
    /// Where an upload got to — the read that makes resuming possible. A browser that was interrupted knows what
    /// it was sending and nothing about what arrived.
    /// </summary>
    [HttpGet("uploads/{uploadId}")]
    public async Task<ActionResult<Application.Features.Files.Commands.FileUploadSessionDto>> GetUpload(
        Guid patientId,
        Guid uploadId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new Application.Features.Files.Queries.GetFileUploadQuery { PatientId = patientId, UploadId = uploadId },
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    /// <summary>
    /// One chunk, as a <b>raw body</b> rather than a multipart part.
    ///
    /// <para>⚠️ <c>Request.Body</c> is read directly and streamed to the store: buffering it would put one chunk
    /// per concurrent upload in the server's memory for no benefit, and multipart framing would add a parse over
    /// bytes that carry no fields. <c>Content-Length</c> is the framework's own count of what it received, never
    /// a client header this code trusts — and the handler refuses a part whose length is not the one the session's
    /// arithmetic expects.</para>
    ///
    /// <para>⚠️ The limit is one chunk plus a margin, not the file: a body larger than a chunk is not a large
    /// upload, it is a client that has stopped following the protocol.</para>
    /// </summary>
    [HttpPut("uploads/{uploadId}/chunks/{partNumber:int}")]
    [RequestSizeLimit(FileTypeCatalog.UploadChunkBytes + (64 * 1024))]
    public async Task<ActionResult<Application.Features.Files.Commands.FileUploadSessionDto>> UploadChunk(
        Guid patientId,
        Guid uploadId,
        int partNumber,
        CancellationToken cancellationToken = default)
    {
        var length = Request.ContentLength;
        if (length is null or <= 0)
        {
            return Failure("Le morceau est vide.");
        }

        var command = new Application.Features.Files.Commands.UploadFileChunkCommand
        {
            PatientId = patientId,
            UploadId = uploadId,
            PartNumber = partNumber,
            Length = length.Value,
            Content = Request.Body
        };

        var result = await _mediator.Send(command, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : HandleFailure(result);
    }

    /// <summary>
    /// Assembles the parts and records the file. ⚠️ Sized for the <b>preview</b> alone, on the coffre door's
    /// reasoning: the original is already staged, so the only thing crossing the wire here is a small image, and
    /// the headroom is what lets the handler drop an oversized one rather than Kestrel 413 the whole request and
    /// lose an upload that is entirely present.
    /// </summary>
    [HttpPost("uploads/{uploadId}/complete")]
    [RequestSizeLimit(2 * FileTypeCatalog.PreviewBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = 2 * FileTypeCatalog.PreviewBytes)]
    public async Task<ActionResult<Application.DTOs.PatientFileDto>> CompleteUpload(
        Guid patientId,
        Guid uploadId,
        [FromForm] Models.CompleteFileUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new Application.Features.Files.Commands.CompleteFileUploadCommand
        {
            PatientId = patientId,
            UploadId = uploadId,
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

    /// <summary>Gives up an upload and releases its parts. An upload already gone answers success.</summary>
    [HttpDelete("uploads/{uploadId}")]
    public async Task<IActionResult> AbandonUpload(
        Guid patientId,
        Guid uploadId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new Application.Features.Files.Commands.AbandonFileUploadCommand
            {
                PatientId = patientId,
                UploadId = uploadId
            },
            cancellationToken);

        return result.IsSuccess ? NoContent() : HandleFailure(result);
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

    // ── Repères sur un modèle 3D (mesh-interactive-viewer) ──────────────────────────────────────────────

    // ⚠️ All four stay on the CLASS policy, including the delete — unlike the file deletes below, and the
    // difference is what is destroyed. Removing a scanned document leaves nothing on any screen to say it was
    // ever there; removing a marker takes away a pin somebody dropped a minute ago and can drop again. Gating
    // it behind AdminOrDoctor would mean reception could place a marker and then not tidy it up.
    [HttpGet("{fileId}/annotations")]
    public async Task<ActionResult<List<Application.DTOs.PatientFileAnnotationDto>>> GetAnnotations(
        Guid patientId,
        Guid fileId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetFileAnnotationsQuery { PatientId = patientId, FileId = fileId }, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPost("{fileId}/annotations")]
    public async Task<ActionResult<Application.DTOs.PatientFileAnnotationDto>> CreateAnnotation(
        Guid patientId,
        Guid fileId,
        [FromBody] CreateFileAnnotationCommand command,
        CancellationToken cancellationToken = default)
    {
        command.PatientId = patientId;
        command.FileId = fileId;
        // From the token, never the body — the same source `UploadedBy` takes.
        command.CreatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpPut("{fileId}/annotations/{annotationId}")]
    public async Task<ActionResult<Application.DTOs.PatientFileAnnotationDto>> RenameAnnotation(
        Guid patientId,
        Guid fileId,
        Guid annotationId,
        [FromBody] RenameFileAnnotationCommand command,
        CancellationToken cancellationToken = default)
    {
        command.PatientId = patientId;
        command.FileId = fileId;
        command.AnnotationId = annotationId;

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{fileId}/annotations/{annotationId}")]
    public async Task<IActionResult> DeleteAnnotation(
        Guid patientId,
        Guid fileId,
        Guid annotationId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new DeleteFileAnnotationCommand
            {
                PatientId = patientId,
                FileId = fileId,
                AnnotationId = annotationId
            },
            cancellationToken);

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

