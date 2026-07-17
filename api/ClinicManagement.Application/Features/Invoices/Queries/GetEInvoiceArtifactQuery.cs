using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Queries;

/// <summary>Which e-invoicing legal artifact to download for a validated invoice (US-4).</summary>
public enum EInvoiceArtifactType
{
    SignedXml = 0,
    TtnReceipt = 1
}

/// <summary>Download the signed TEIF XML or the TTN receipt stored for an invoice (US-4).</summary>
public class GetEInvoiceArtifactQuery : IRequest<Result<EInvoiceArtifactResult>>
{
    public Guid Id { get; set; }
    public EInvoiceArtifactType ArtifactType { get; set; }
}

public class GetEInvoiceArtifactQueryHandler : IRequestHandler<GetEInvoiceArtifactQuery, Result<EInvoiceArtifactResult>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<GetEInvoiceArtifactQueryHandler> _logger;

    public GetEInvoiceArtifactQueryHandler(
        IInvoiceRepository invoiceRepository,
        ICurrentClinicResolver clinicResolver,
        IFileStorage fileStorage,
        ILogger<GetEInvoiceArtifactQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _clinicResolver = clinicResolver;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Result<EInvoiceArtifactResult>> Handle(GetEInvoiceArtifactQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<EInvoiceArtifactResult>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var invoice = await _invoiceRepository.GetByIdAsync(request.Id, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicResult.Value)
            {
                return Result<EInvoiceArtifactResult>.Failure("Facture introuvable.");
            }

            var (storageKey, fileName, contentType) = request.ArtifactType == EInvoiceArtifactType.SignedXml
                ? (invoice.SignedXmlStorageKey, $"teif-{invoice.Number}.xml", "application/xml")
                : (invoice.TtnReceiptStorageKey, $"recu-ttn-{invoice.Number}.xml", "application/xml");

            if (string.IsNullOrWhiteSpace(storageKey))
            {
                return Result<EInvoiceArtifactResult>.Failure("Document indisponible pour cette facture.");
            }

            await using var stream = await _fileStorage.DownloadAsync(storageKey, cancellationToken);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);

            return Result<EInvoiceArtifactResult>.Success(new EInvoiceArtifactResult
            {
                Content = memory.ToArray(),
                FileName = fileName,
                ContentType = contentType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading e-invoice artifact for invoice {InvoiceId}", request.Id);
            return Result<EInvoiceArtifactResult>.Failure("Erreur lors du téléchargement du document.");
        }
    }
}
