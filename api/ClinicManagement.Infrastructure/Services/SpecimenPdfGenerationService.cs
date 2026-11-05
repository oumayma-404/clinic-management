using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The demo's PDF renderer: the real one, then <see cref="SpecimenStamp"/> on whatever it returns. Every PDF the app
/// produces comes out of <see cref="IPdfGenerationService"/>, so wrapping it is the one place no document can miss.
/// </summary>
public sealed class SpecimenPdfGenerationService : IPdfGenerationService
{
    private readonly IPdfGenerationService _inner;

    public SpecimenPdfGenerationService(IPdfGenerationService inner) => _inner = inner;

    // Only the read-only demo is wrapped; every other deployment gets its renderer untouched.
    public static IPdfGenerationService For(IDemoPolicy demo, IPdfGenerationService inner) =>
        demo.RefusesWrites ? new SpecimenPdfGenerationService(inner) : inner;

    public async Task<byte[]> GeneratePdfFromDocumentDataAsync(
        MedicalDocumentPdfData documentData, CancellationToken cancellationToken = default) =>
        SpecimenStamp.Apply(await _inner.GeneratePdfFromDocumentDataAsync(documentData, cancellationToken));

    public async Task<byte[]> GeneratePdfWithLetterheadAsync(
        MedicalDocumentPdfData documentData, LetterheadImages letterhead, CancellationToken cancellationToken = default) =>
        SpecimenStamp.Apply(await _inner.GeneratePdfWithLetterheadAsync(documentData, letterhead, cancellationToken));

    public async Task<byte[]> GenerateInvoicePdfAsync(InvoicePdfData invoiceData, CancellationToken cancellationToken = default) =>
        SpecimenStamp.Apply(await _inner.GenerateInvoicePdfAsync(invoiceData, cancellationToken));

    public async Task<byte[]> GenerateDevisPdfAsync(DevisPdfData devisData, CancellationToken cancellationToken = default) =>
        SpecimenStamp.Apply(await _inner.GenerateDevisPdfAsync(devisData, cancellationToken));

    public async Task<byte[]> GenerateReceiptPdfAsync(ReceiptPdfData receiptData, CancellationToken cancellationToken = default) =>
        SpecimenStamp.Apply(await _inner.GenerateReceiptPdfAsync(receiptData, cancellationToken));

    public async Task<byte[]> GenerateAvoirPdfAsync(AvoirPdfData avoirData, CancellationToken cancellationToken = default) =>
        SpecimenStamp.Apply(await _inner.GenerateAvoirPdfAsync(avoirData, cancellationToken));
}
