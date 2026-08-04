using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Application.Features.Documents.Queries;
using ClinicManagement.Application.Features.Invoices.Queries;
using ClinicManagement.Application.Features.TreatmentPlans.Queries;
using ClinicManagement.Domain.Entities;
using MediatR;

namespace ClinicManagement.Application.Features.DocumentEmails;

/// <summary>The rendered PDF to attach, and the filename the recipient sees.</summary>
public sealed record DocumentEmailAttachmentResult(byte[] Content, string FileName);

/// <summary>
/// Renders the PDF for one sendable document kind, by <b>delegating to the query that already renders it</b> —
/// <see cref="GetInvoicePdfQuery"/>, <see cref="GetCreditNotePdfQuery"/>, <see cref="GetDevisPdfQuery"/> and the
/// two receipt queries. Nothing about how a document looks is reimplemented here: those handlers own the PDF
/// assembly, the French filenames, the tenant check <i>and</i> the business refusals (« Émettez la facture avant
/// de générer le PDF », a voided receipt's « REÇU ANNULÉ » stamp), so an emailed document is byte-identical to
/// the downloaded one and cannot diverge from it.
/// <para>
/// ⚠️ This runs <b>in the request</b>, never in the dispatcher job: every one of those queries resolves the
/// clinic from the caller's token, which a Hangfire job has no way to supply. That is why the attachment is
/// rendered at queue time and stored, rather than re-rendered at send time.
/// </para>
/// </summary>
public interface IDocumentEmailAttachmentRenderer
{
    Task<Result<DocumentEmailAttachmentResult>> RenderAsync(
        string documentKind,
        Guid documentId,
        Guid? installmentId,
        Guid? paymentId,
        CancellationToken cancellationToken = default);
}

public class DocumentEmailAttachmentRenderer : IDocumentEmailAttachmentRenderer
{
    private readonly IMediator _mediator;
    private readonly IPdfGenerationService _pdfGenerationService;

    public DocumentEmailAttachmentRenderer(IMediator mediator, IPdfGenerationService pdfGenerationService)
    {
        _mediator = mediator;
        _pdfGenerationService = pdfGenerationService;
    }

    public async Task<Result<DocumentEmailAttachmentResult>> RenderAsync(
        string documentKind,
        Guid documentId,
        Guid? installmentId,
        Guid? paymentId,
        CancellationToken cancellationToken = default)
    {
        return documentKind switch
        {
            DocumentEmail.KindMedicalDocument => await RenderMedicalDocumentAsync(documentId, cancellationToken),
            DocumentEmail.KindInvoice => Map(await _mediator.Send(new GetInvoicePdfQuery { Id = documentId }, cancellationToken), r => r.Content, r => r.FileName),
            DocumentEmail.KindCreditNote => Map(await _mediator.Send(new GetCreditNotePdfQuery { Id = documentId }, cancellationToken), r => r.Content, r => r.FileName),
            DocumentEmail.KindTreatmentPlan => Map(await _mediator.Send(new GetDevisPdfQuery { Id = documentId }, cancellationToken), r => r.Content, r => r.FileName),
            DocumentEmail.KindInvoicePaymentReceipt => await RenderInvoiceReceiptAsync(paymentId, cancellationToken),
            DocumentEmail.KindInstallmentPaymentReceipt => await RenderInstallmentReceiptAsync(documentId, installmentId, paymentId, cancellationToken),
            _ => Result<DocumentEmailAttachmentResult>.Failure("Type de document non pris en charge pour l'envoi par email.")
        };
    }

    // A medical document has no by-id PDF query (the download endpoint takes a body), so it goes through the
    // read + the shared PdfGenerationJob mapping. GetMedicalDocumentQuery is the tenant check.
    private async Task<Result<DocumentEmailAttachmentResult>> RenderMedicalDocumentAsync(
        Guid documentId, CancellationToken cancellationToken)
    {
        var documentResult = await _mediator.Send(new GetMedicalDocumentQuery { Id = documentId }, cancellationToken);
        if (documentResult.IsFailure || documentResult.Value == null)
        {
            return Result<DocumentEmailAttachmentResult>.Failure(documentResult.Error ?? "Document introuvable.");
        }

        var document = documentResult.Value;
        var pdfData = MedicalDocumentPdfMapping.ToPdfData(document);
        var bytes = await _pdfGenerationService.GeneratePdfFromDocumentDataAsync(pdfData, cancellationToken);

        // Mirrors the download endpoint's naming so the recipient sees the same filename either way.
        var patientSlug = Slug(document.PatientName);
        var fileName = $"{document.DocumentType.ToLowerInvariant()}-{patientSlug}.pdf";

        return Result<DocumentEmailAttachmentResult>.Success(new DocumentEmailAttachmentResult(bytes, fileName));
    }

    private async Task<Result<DocumentEmailAttachmentResult>> RenderInvoiceReceiptAsync(
        Guid? paymentId, CancellationToken cancellationToken)
    {
        if (paymentId is null || paymentId == Guid.Empty)
        {
            return Result<DocumentEmailAttachmentResult>.Failure("Le paiement du reçu est obligatoire.");
        }

        var result = await _mediator.Send(new GetPaymentReceiptPdfQuery { PaymentId = paymentId.Value }, cancellationToken);
        return Map(result, r => r.Content, r => r.FileName);
    }

    private async Task<Result<DocumentEmailAttachmentResult>> RenderInstallmentReceiptAsync(
        Guid planId, Guid? installmentId, Guid? paymentId, CancellationToken cancellationToken)
    {
        if (installmentId is null || installmentId == Guid.Empty || paymentId is null || paymentId == Guid.Empty)
        {
            return Result<DocumentEmailAttachmentResult>.Failure("L'échéance et le paiement du reçu sont obligatoires.");
        }

        var result = await _mediator.Send(
            new GetInstallmentReceiptPdfQuery
            {
                PlanId = planId,
                InstallmentId = installmentId.Value,
                PaymentId = paymentId.Value
            },
            cancellationToken);

        return Map(result, r => r.Content, r => r.FileName);
    }

    // Lifts a renderer's own Result<T> into the attachment shape, preserving its French failure message — the
    // refusals those handlers produce are the ones the practitioner needs to read.
    private static Result<DocumentEmailAttachmentResult> Map<T>(
        Result<T> result, Func<T, byte[]> content, Func<T, string> fileName)
    {
        if (result.IsFailure || result.Value == null)
        {
            return Result<DocumentEmailAttachmentResult>.Failure(result.Error ?? "Erreur lors de la génération du PDF.");
        }

        return Result<DocumentEmailAttachmentResult>.Success(
            new DocumentEmailAttachmentResult(content(result.Value), fileName(result.Value)));
    }

    private static string Slug(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "document" : value.ToLowerInvariant().Replace(" ", "-");
}
