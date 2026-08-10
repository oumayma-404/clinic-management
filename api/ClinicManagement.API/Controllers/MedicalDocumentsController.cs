using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediatR;
using ClinicManagement.Application.Common.Authorization;
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
//
// `AnyClinicRole` (was `AdminOrDoctor`): ordonnances, certificats, lettres de liaison, bulletins CNAM and
// arrêts de travail are typed at the desk in a Tunisian cabinet, and the whole « Documents » screen 403'd for
// a secretary. `DELETE` still tightens to `AdminOrDoctor` — see the attribute on DeleteDocument.
//
// ⚠️ Opening authorship required fixing what used to make it safe by accident. The cachet and the n° d'ordre
// CNOMDT were resolved from the *caller's* own Doctor record, so a document authored by anyone without one
// rendered with no practitioner identity at all — silently, on a form whose whole purpose is to carry it.
// They are now resolved from the practitioner the editor **chose** (`IssuingDoctorId`), validated against this
// clinic's roster, with the caller's own record as the fall-back. See PractitionerRenderSnapshot.ResolveAsync.
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class MedicalDocumentsController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<MedicalDocumentsController> _logger;

    public MedicalDocumentsController(IMediator mediator, ILogger<MedicalDocumentsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
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
                IssuingDoctorId = Guid.TryParse(GetFormValue("issuingDoctorId"), out var issuingDoctorIdValue) ? issuingDoctorIdValue : null,
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
                return Failure("Requête invalide.");
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
            AppointmentId = request.AppointmentId,
            IssuingDoctorId = request.IssuingDoctorId
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
                IssuingDoctorId = Guid.TryParse(GetFormValue("issuingDoctorId"), out var issuingDoctorIdValue) ? issuingDoctorIdValue : null,
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
                return Failure("Requête invalide.");
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

    /// <summary>
    /// Delete a medical document (ordonnance, certificat, lettre de liaison…) and its stored blob.
    /// `AdminOrDoctor` — the document is a signed clinical instrument issued in a practitioner's name; the
    /// class-level <c>[Authorize]</c> was the only gate (audit adjacent defect A-12), so a secretary could
    /// destroy an ordonnance.
    ///
    /// <para>⚠️ Since the class opened to <c>AnyClinicRole</c> this attribute is the only gate on the delete,
    /// and the distinction it draws is the feature's: reception <b>writes</b> the cabinet's documents and does
    /// not <b>destroy</b> them. Note this also deletes the blob, so there is nothing to restore afterwards.</para>
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrDoctor)]
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
            return Failure("Document introuvable.", StatusCodes.Status404NotFound);
        }

        var document = documentResult.Value;

        // Queue background job for PDF generation
        var jobId = BackgroundJob.Enqueue<PdfGenerationJob>(
            job => job.GenerateAndAttachPdfAsync(id, cancellationToken));

        return Ok(new { JobId = jobId, Message = "PDF generation queued successfully" });
    }

    [HttpPost("generate-pdf-download")]
    [AllowsWithoutSubscription("AC-4.3, AC-4.9 — renders a document the cabinet already holds for immediate download.")]
    public async Task<ActionResult> GeneratePdfForDownload(
        [FromBody] Application.Common.Models.MedicalDocumentPdfData documentData,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Part C (FR-3.2/FR-3.3/FR-6.1) + security: the cachet storage key, its content type, the CNOMDT
            // ordre and the cabinet city are authoritative server-side values — they must NEVER be trusted
            // from the client body (a caller-supplied DoctorCachetKey would let them embed another
            // practitioner's cachet). Clear any client-provided values first, then overlay the server-resolved
            // snapshot. Best-effort: a resolution failure renders without a cachet/city/ordre — never with a
            // client-injected one.
            //
            // IssuingDoctorId is deliberately NOT cleared: it selects which of the caller's own clinic's
            // practitioners to resolve (tenant-checked in PractitionerRenderSnapshot.ResolveAsync) and carries no
            // value of its own. Clearing it would put us back to resolving from the caller, which is what printed
            // an ordonnance with no cachet whenever the person at the keyboard was not the prescriber.
            documentData.DoctorCachetKey = null;
            documentData.DoctorCachetContentType = null;
            documentData.DoctorOrdreNumber = null;
            documentData.ClinicCity = null;
            documentData.ClinicEmail = null;

            var snapshotResult = await _mediator.Send(
                new GetPractitionerRenderSnapshotQuery { IssuingDoctorId = documentData.IssuingDoctorId },
                cancellationToken);
            if (snapshotResult.IsSuccess && snapshotResult.Value != null)
            {
                var snap = snapshotResult.Value;
                documentData.ClinicCity = snap.ClinicCity;
                documentData.ClinicEmail = snap.ClinicEmail;
                documentData.DoctorOrdreNumber = snap.DoctorOrdreNumber;
                documentData.DoctorCachetKey = snap.DoctorCachetKey;
                documentData.DoctorCachetContentType = snap.DoctorCachetContentType;
            }

            var pdfService = HttpContext.RequestServices.GetRequiredService<ClinicManagement.Application.Common.Interfaces.IPdfGenerationService>();
            var pdfBytes = await pdfService.GeneratePdfFromDocumentDataAsync(documentData, cancellationToken);
            
            var fileName = $"{documentData.DocumentType.ToLowerInvariant()}-{documentData.PatientName.ToLowerInvariant().Replace(" ", "-")}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            // Was `BadRequest($"Error generating PDF: {ex.Message}")` — a bare JSON *string*, not the canonical
            // `{ error }` body. The client's `generatePdfForDownload` therefore threw a plain `Error` rather than
            // an `ApiError`, and `handleDownloadPdf` only surfaces a message `if (error instanceof ApiError)`, so
            // the three deliberate French operator messages on this path (a missing or unreadable `Assets/BS1.pdf`,
            // no system font for the overlay) were **structurally unreachable** — the dentist got a generic toast
            // for a problem with a named remedy.
            //
            // Only `InvalidOperationException` is surfaced verbatim: that is the type those three fail-fast
            // messages use, and they are written for an operator. Anything else is generic — an arbitrary
            // exception message is a .NET internal, not French, and can carry a path or a connection string.
            _logger.LogError(ex, "Failed to render a document PDF for download ({DocumentType})", documentData.DocumentType);
            return ex is InvalidOperationException
                ? Failure(ex.Message)
                : Failure(ClinicManagement.Application.Common.ErrorMessages.Generic);
        }
    }
}

