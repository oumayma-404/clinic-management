using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Application.Features.Documents.Queries;
using MediatR;
using Microsoft.Extensions.Logging;
using Hangfire;
using System.Text.Json;

namespace ClinicManagement.API.BackgroundJobs;

public class PdfGenerationJob
{
    private readonly IPdfGenerationService _pdfService;
    private readonly IMediator _mediator;
    private readonly ILogger<PdfGenerationJob> _logger;

    public PdfGenerationJob(
        IPdfGenerationService pdfService,
        IMediator mediator,
        ILogger<PdfGenerationJob> logger)
    {
        _pdfService = pdfService;
        _mediator = mediator;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task GenerateAndAttachPdfAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
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

            // Parse content JSON
            var content = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(document.ContentJson) 
                ?? new Dictionary<string, JsonElement>();

            // Build PDF data from document
            var pdfData = new MedicalDocumentPdfData
            {
                DocumentType = document.DocumentType,
                DocumentDate = document.DocumentDate,
                PatientName = document.PatientName,
                PatientAge = document.PatientAge,
                ClinicName = document.ClinicName,
                ClinicAddress = document.ClinicAddress,
                ClinicPhone = document.ClinicPhone,
                DoctorName = document.DoctorName,
                DoctorSpecialty = document.DoctorSpecialty,
                RecipientDoctorName = document.RecipientDoctorName,
                RecipientDoctorSpecialty = document.RecipientDoctorSpecialty,
                Content = content.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ValueKind == JsonValueKind.String 
                        ? kvp.Value.GetString() ?? "" 
                        : JsonSerializer.Serialize(kvp.Value)) // Properly serialize JSON arrays/objects to string
            };

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

