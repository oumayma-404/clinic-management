using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Application.Features.Documents.Queries;
using ClinicManagement.API.Models;
using ClinicManagement.API.BackgroundJobs;
using Microsoft.AspNetCore.Http;
using Hangfire;
using System;

namespace ClinicManagement.API.Controllers;

[ApiController]
[Route("api/medical-documents")]
// Explicit defense-in-depth: this controller was previously anonymous-by-omission and serves patient
// PHI (medical documents + generated PDFs). The class-level [Authorize] documents the intent and
// authenticates it in BOTH modes' terms — in Local mode it is also covered by the fail-closed fallback
// policy (FR-E3); in Cloud it now requires the Auth0 bearer the frontend already sends (verified: the
// one raw-fetch caller attaches the token).
[Authorize]
public class MedicalDocumentsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public MedicalDocumentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Application.DTOs.MedicalDocumentDto>>> GetDocuments(
        [FromQuery] Guid? patientId,
        [FromQuery] string? documentType,
        CancellationToken cancellationToken = default)
    {
        var query = new GetMedicalDocumentsQuery
        {
            PatientId = patientId,
            DocumentType = documentType
        };

        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Application.DTOs.MedicalDocumentDto>> GetDocument(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var query = new GetMedicalDocumentQuery { Id = id };
        var result = await _mediator.Send(query, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result, StatusCodes.Status404NotFound);
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<ActionResult<Application.DTOs.MedicalDocumentDto>> CreateDocument(
        CancellationToken cancellationToken = default)
    {
        CreateMedicalDocumentRequest request;
        IFormFile? pdfFile = null;

        // Check content type to determine if it's form data or JSON
        if (Request.ContentType?.Contains("multipart/form-data") == true)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            
            // Helper to safely get form value
            string? GetFormValue(string key)
            {
                if (form[key].Count > 0)
                {
                    return form[key].ToString();
                }
                return null;
            }
            
            request = new CreateMedicalDocumentRequest
            {
                PatientId = Guid.Parse(GetFormValue("patientId") ?? throw new ArgumentException("patientId is required")),
                DocumentType = GetFormValue("documentType") ?? throw new ArgumentException("documentType is required"),
                DocumentDate = DateTime.Parse(GetFormValue("documentDate") ?? throw new ArgumentException("documentDate is required")),
                RecipientDoctorName = GetFormValue("recipientDoctorName"),
                RecipientDoctorSpecialty = GetFormValue("recipientDoctorSpecialty"),
                ContentJson = GetFormValue("contentJson") ?? throw new ArgumentException("contentJson is required"),
                ClinicName = GetFormValue("clinicName") ?? throw new ArgumentException("clinicName is required"),
                ClinicAddress = GetFormValue("clinicAddress") ?? throw new ArgumentException("clinicAddress is required"),
                ClinicPhone = GetFormValue("clinicPhone") ?? throw new ArgumentException("clinicPhone is required"),
                DoctorName = GetFormValue("doctorName") ?? throw new ArgumentException("doctorName is required"),
                DoctorSpecialty = GetFormValue("doctorSpecialty") ?? throw new ArgumentException("doctorSpecialty is required"),
                AppointmentId = Guid.TryParse(GetFormValue("appointmentId"), out var appointmentIdValue) ? appointmentIdValue : null,
                PdfFile = null // Will be set separately
            };
            
            if (form.Files["pdfFile"] != null)
            {
                pdfFile = form.Files["pdfFile"];
                // Log file info for debugging
                System.Diagnostics.Debug.WriteLine($"PDF file received: {pdfFile.FileName}, Size: {pdfFile.Length} bytes, ContentType: {pdfFile.ContentType}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No PDF file in form data");
            }
        }
        else
        {
            request = await Request.ReadFromJsonAsync<CreateMedicalDocumentRequest>(cancellationToken);
            if (request == null)
            {
                return BadRequest("Invalid request body");
            }
        }

        // Map request to command
        var command = new CreateMedicalDocumentCommand
        {
            PatientId = request.PatientId,
            DocumentType = request.DocumentType,
            DocumentDate = request.DocumentDate,
            RecipientDoctorName = request.RecipientDoctorName,
            RecipientDoctorSpecialty = request.RecipientDoctorSpecialty,
            ContentJson = request.ContentJson,
            ClinicName = request.ClinicName,
            ClinicAddress = request.ClinicAddress,
            ClinicPhone = request.ClinicPhone,
            DoctorName = request.DoctorName,
            DoctorSpecialty = request.DoctorSpecialty,
            AppointmentId = request.AppointmentId
        };

        // Read PDF file if provided
        if (pdfFile != null && pdfFile.Length > 0)
        {
            using var memoryStream = new MemoryStream();
            await pdfFile.CopyToAsync(memoryStream, cancellationToken);
            command.PdfFile = memoryStream.ToArray();
        }

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return CreatedAtAction(nameof(GetDocument), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Application.DTOs.MedicalDocumentDto>> UpdateDocument(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        UpdateMedicalDocumentCommand command;

        // Check content type to determine if it's form data or JSON
        if (Request.ContentType?.Contains("multipart/form-data") == true)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            
            // Helper to safely get form value
            string? GetFormValue(string key)
            {
                if (form[key].Count > 0)
                {
                    return form[key].ToString();
                }
                return null;
            }
            
            command = new UpdateMedicalDocumentCommand
            {
                Id = id,
                DocumentDate = DateTime.Parse(GetFormValue("documentDate") ?? throw new ArgumentException("documentDate is required")),
                RecipientDoctorName = GetFormValue("recipientDoctorName"),
                RecipientDoctorSpecialty = GetFormValue("recipientDoctorSpecialty"),
                ContentJson = GetFormValue("contentJson") ?? throw new ArgumentException("contentJson is required"),
                FileId = Guid.TryParse(GetFormValue("fileId"), out var fileIdValue) ? (Guid?)fileIdValue : null,
                PdfFile = null // Will be set separately
            };
            
            // Read PDF file if provided
            if (form.Files["pdfFile"] != null)
            {
                using var memoryStream = new MemoryStream();
                await form.Files["pdfFile"].CopyToAsync(memoryStream, cancellationToken);
                command.PdfFile = memoryStream.ToArray();
            }
        }
        else
        {
            command = await Request.ReadFromJsonAsync<UpdateMedicalDocumentCommand>(cancellationToken);
            if (command == null)
            {
                return BadRequest("Invalid request body");
            }
            command.Id = id;
        }

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return Ok(result.Value);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDocument(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var command = new DeleteMedicalDocumentCommand { Id = id };
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleFailure(result);
        }

        return NoContent();
    }

    [HttpPost("{id}/generate-pdf")]
    public async Task<ActionResult> GeneratePdf(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Get document data
        var getQuery = new GetMedicalDocumentQuery { Id = id };
        var documentResult = await _mediator.Send(getQuery, cancellationToken);

        if (!documentResult.IsSuccess || documentResult.Value == null)
        {
            return NotFound("Document not found");
        }

        var document = documentResult.Value;

        // Queue background job for PDF generation
        var jobId = BackgroundJob.Enqueue<PdfGenerationJob>(
            job => job.GenerateAndAttachPdfAsync(id, cancellationToken));

        return Ok(new { JobId = jobId, Message = "PDF generation queued successfully" });
    }

    [HttpPost("generate-pdf-download")]
    public async Task<ActionResult> GeneratePdfForDownload(
        [FromBody] Application.Common.Models.MedicalDocumentPdfData documentData,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pdfService = HttpContext.RequestServices.GetRequiredService<ClinicManagement.Application.Common.Interfaces.IPdfGenerationService>();
            var pdfBytes = await pdfService.GeneratePdfFromDocumentDataAsync(documentData, cancellationToken);
            
            var fileName = $"{documentData.DocumentType.ToLowerInvariant()}-{documentData.PatientName.ToLowerInvariant().Replace(" ", "-")}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error generating PDF: {ex.Message}");
        }
    }
}

