using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Common.Interfaces;

public interface IPdfGenerationService
{
    Task<byte[]> GeneratePdfFromDocumentDataAsync(MedicalDocumentPdfData documentData, CancellationToken cancellationToken = default);

    /// <summary>Render a Tunisian note-d'honoraires (numbered invoice) to PDF — amounts in TND.</summary>
    Task<byte[]> GenerateInvoicePdfAsync(InvoicePdfData invoiceData, CancellationToken cancellationToken = default);

    /// <summary>Render a dental devis (quote) to PDF — a non-fiscal estimate, amounts in TND.</summary>
    Task<byte[]> GenerateDevisPdfAsync(DevisPdfData devisData, CancellationToken cancellationToken = default);
}

