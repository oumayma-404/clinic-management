using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Application.Features.Files.Queries;
using System.Security.Claims;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Domain.Common;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/patients/{patientId}/files")]
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class PatientFilesController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<PatientFilesController> _logger;

    public PatientFilesController(IMediator mediator, ILogger<PatientFilesController> logger)
    {
        _mediator = mediator;
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
            UploadedBy = uploadedBy
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetFiles), new { patientId }, result.Value);
    }

    [HttpGet("{fileId}/download")]
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

