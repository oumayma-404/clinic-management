using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Features.Documents.Queries;

/// <summary>
/// Renders a <b>stored</b> medical document to PDF, by id.
///
/// <para>
/// ⚠️ <b>The gap this closes.</b> Until now the only way to see a saved document's PDF was
/// <c>generate-pdf-download</c>, which takes the <i>whole document in the body</i> — so any surface wanting to
/// show one had to re-compose it in the browser: flatten <c>ContentJson</c> into string values, re-send
/// <c>clinicName</c> / <c>doctorName</c>, and get the flattening right. That is a second copy of
/// <c>MedicalDocumentPdfMapping.FlattenContent</c> per call site, and it is how the editor came to post
/// literal <c>"[Nom du cabinet]"</c> to a legal document. A read of a document that already exists should be a
/// GET with an id, and now it is.
/// </para>
///
/// <para>
/// It renders through <c>GetMedicalDocumentQuery</c> (which carries the tenant check) and then the same
/// <c>ToPdfData</c> path <c>PdfGenerationJob</c> and the document-email command take, so all three produce the
/// same bytes. Nothing is stored: the attached-PDF copy is the job's business.
/// </para>
/// </summary>
public class GetMedicalDocumentPdfQuery : IRequest<Result<MedicalDocumentPdfFileDto>>
{
    public Guid Id { get; set; }
}

/// <summary>The rendered document, with the French filename <c>DocumentFileNaming</c> decides.</summary>
public class MedicalDocumentPdfFileDto
{
    public byte[] Pdf { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
}

public class GetMedicalDocumentPdfQueryHandler
    : IRequestHandler<GetMedicalDocumentPdfQuery, Result<MedicalDocumentPdfFileDto>>
{
    private readonly IMediator _mediator;
    private readonly IPdfGenerationService _pdfGenerationService;
    private readonly ILogger<GetMedicalDocumentPdfQueryHandler> _logger;

    public GetMedicalDocumentPdfQueryHandler(
        IMediator mediator,
        IPdfGenerationService pdfGenerationService,
        ILogger<GetMedicalDocumentPdfQueryHandler> logger)
    {
        _mediator = mediator;
        _pdfGenerationService = pdfGenerationService;
        _logger = logger;
    }

    public async Task<Result<MedicalDocumentPdfFileDto>> Handle(
        GetMedicalDocumentPdfQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Through the query rather than the repository: that is where the « document médical introuvable »
            // tenant check lives, and a second ownership test here would be a second answer to it.
            var document = await _mediator.Send(new GetMedicalDocumentQuery { Id = request.Id }, cancellationToken);
            if (!document.IsSuccess || document.Value is null)
            {
                return Result<MedicalDocumentPdfFileDto>.FailureFrom(document);
            }

            var pdfData = MedicalDocumentPdfMapping.ToPdfData(document.Value);
            var pdfBytes = await _pdfGenerationService.GeneratePdfFromDocumentDataAsync(pdfData, cancellationToken);

            var patientSlug = document.Value.PatientName.Trim().ToLowerInvariant().Replace(" ", "-");
            return Result<MedicalDocumentPdfFileDto>.Success(new MedicalDocumentPdfFileDto
            {
                Pdf = pdfBytes,
                FileName =
                    $"{DocumentFileNaming.GetDocumentTypeName(document.Value.DocumentType)}-{patientSlug}.pdf",
            });
        }
        catch (InvalidOperationException ex)
        {
            // Typed on purpose — the renderer's fail-fast messages are French and written for an operator
            // (a missing or unreadable official form asset, no system font). See PreviewFicheOrdonnanceQuery.
            _logger.LogError(ex, "Failed to render medical document {DocumentId} to PDF", request.Id);
            return Result<MedicalDocumentPdfFileDto>.Failure(ex.Message, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render medical document {DocumentId} to PDF", request.Id);
            return Result<MedicalDocumentPdfFileDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
