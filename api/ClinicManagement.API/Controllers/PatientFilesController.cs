using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Application.Features.Files.Queries;
using System.Security.Claims;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/patients/{patientId}/files")]
[Authorize]
public class PatientFilesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<PatientFilesController> _logger;

    public PatientFilesController(IMediator mediator, ILogger<PatientFilesController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("folders/initialize-defaults")]
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
            return BadRequest(result.Error);
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
            return BadRequest(result.Error);
        }

        return Ok(result.Value);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Application.DTOs.PatientFileDto>>> GetFiles(
        Guid patientId,
        [FromQuery] Guid? folderId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetPatientFilesQuery
        {
            PatientId = patientId,
            FolderId = folderId
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(result.Error);
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
            return BadRequest(result.Error);
        }

        return CreatedAtAction(nameof(GetFolders), new { patientId }, result.Value);
    }

    [HttpPost("upload")]
    public async Task<ActionResult<Application.DTOs.PatientFileDto>> UploadFile(
        Guid patientId,
        [FromForm] Models.UploadFileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest("File is required");
        }

        var uploadedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

        var command = new UploadPatientFileCommand
        {
            PatientId = patientId,
            FolderId = request.FolderId,
            FileName = request.File.FileName,
            ContentType = request.File.ContentType,
            FileSize = request.File.Length,
            FileStream = request.File.OpenReadStream(),
            Description = request.Description,
            UploadedBy = uploadedBy
        };

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(result.Error);
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
            return BadRequest(result.Error);
        }

        var fileDto = result.Value!;
        return File(fileDto.FileStream, fileDto.ContentType, fileDto.FileName);
    }

    [HttpDelete("{fileId}")]
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
            return BadRequest(result.Error);
        }

        return NoContent();
    }

    [HttpDelete("folders/{folderId}")]
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
            return BadRequest(result.Error);
        }

        return NoContent();
    }
}

