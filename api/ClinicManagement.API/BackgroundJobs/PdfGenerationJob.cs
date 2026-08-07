using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Application.Features.Documents.Queries;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using Hangfire;

namespace ClinicManagement.API.BackgroundJobs;

public class PdfGenerationJob
{
    private readonly IPdfGenerationService _pdfService;
    private readonly IMediator _mediator;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly IMedicalDocumentRepository _documents;
    private readonly ILogger<PdfGenerationJob> _logger;

    public PdfGenerationJob(
        IPdfGenerationService pdfService,
        IMediator mediator,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        IMedicalDocumentRepository documents,
        ILogger<PdfGenerationJob> logger)
    {
        _pdfService = pdfService;
        _mediator = mediator;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _documents = documents;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task GenerateAndAttachPdfAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        // I6: a job has no token, so without naming itself every row it writes would read « Tâche automatique »
        // with no clue which one. The declaration happens before anything is saved — see IAuditActorProvider.RunAs.
        _auditActor.RunAs(nameof(PdfGenerationJob));

        // US-2: this renders ONE document, so it scopes to that document's clinic rather than declaring itself
        // cross-clinic — SystemWide would switch the backstop off for the whole scope to render a single PDF.
        // Resolving the owner is the one read that must precede the scope, hence the scope-independent lookup.
        var owningClinicId = await _documents.GetOwningClinicIdAsync(documentId, cancellationToken);
        if (owningClinicId is null)
        {
            _logger.LogError("Document {DocumentId} has no resolvable clinic; not rendering.", documentId);
            throw new InvalidOperationException($"Document {documentId} has no resolvable clinic.");
        }

        _tenantScope.UseClinic(owningClinicId.Value);

        try
        {
            _logger.LogInformation("Starting PDF generation for document {DocumentId}", documentId);

            // Get current document to preserve existing fields
            var getQuery = new GetMedicalDocumentQuery { Id = documentId };
            var documentResult = await _mediator.Send(getQuery, cancellationToken);

            if (!documentResult.IsSuccess || documentResult.Value == null)
            {
                _logger.LogError("Document {DocumentId} not found", documentId);
                throw new Exception($"Document {documentId} not found");
            }

            var document = documentResult.Value;

            // Shared with the document-email queue command so both render the same bytes for a given document
            // (Part C snapshot fields included) — see MedicalDocumentPdfMapping.
            var pdfData = MedicalDocumentPdfMapping.ToPdfData(document);

            // Generate PDF from structured data
            var pdfBytes = await _pdfService.GeneratePdfFromDocumentDataAsync(pdfData, cancellationToken);

            _logger.LogInformation("PDF generated successfully for document {DocumentId}, size: {Size} bytes", documentId, pdfBytes.Length);

            // Update document with PDF file
            // When explicitly saving PDF to files (via background job), always save to "documents" folder
            var updateCommand = new UpdateMedicalDocumentCommand
            {
                Id = documentId,
                DocumentDate = document.DocumentDate,
                ContentJson = document.ContentJson,
                RecipientDoctorName = document.RecipientDoctorName,
                RecipientDoctorSpecialty = document.RecipientDoctorSpecialty,
                PdfFile = pdfBytes // This will trigger the file upload logic
            };
            
            _logger.LogInformation("Updating document {DocumentId} with PDF", documentId);

            var result = await _mediator.Send(updateCommand, cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to attach PDF to document {DocumentId}: {Error}", documentId, result.Error);
                throw new Exception($"Failed to attach PDF: {result.Error}");
            }

            _logger.LogInformation("PDF attached successfully to document {DocumentId}", documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF for document {DocumentId}", documentId);
            throw;
        }
    }
}

