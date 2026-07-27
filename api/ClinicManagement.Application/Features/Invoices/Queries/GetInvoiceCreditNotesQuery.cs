using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Queries;

/// <summary>
/// The avoirs established against one invoice, newest first.
///
/// <para>
/// There was no read path for a credit note at all: <c>ICreditNoteRepository</c> exposed only a numbering
/// probe, two aggregate sums and <c>AddAsync</c>. An avoir was therefore write-only — established once,
/// counted in the caisse totals, and never visible again in any screen, list or document.
/// </para>
/// </summary>
public class GetInvoiceCreditNotesQuery : IRequest<Result<List<CreditNoteDto>>>
{
    public Guid InvoiceId { get; set; }
}

public class GetInvoiceCreditNotesQueryHandler
    : IRequestHandler<GetInvoiceCreditNotesQuery, Result<List<CreditNoteDto>>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetInvoiceCreditNotesQueryHandler> _logger;

    public GetInvoiceCreditNotesQueryHandler(
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetInvoiceCreditNotesQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _creditNoteRepository = creditNoteRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<List<CreditNoteDto>>> Handle(
        GetInvoiceCreditNotesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<List<CreditNoteDto>>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            // Tenant isolation runs through the INVOICE, not the avoir: an avoir from another clinic can only
            // be reached via an invoice from that clinic, which reads as "not found" here.
            var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                return Result<List<CreditNoteDto>>.Failure("Facture introuvable.");
            }

            var creditNotes = await _creditNoteRepository.GetByInvoiceIdAsync(invoice.Id, cancellationToken);

            return Result<List<CreditNoteDto>>.Success(
                creditNotes.Select(c => c.ToDto(invoice)).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing credit notes for invoice {InvoiceId}", request.InvoiceId);
            return Result<List<CreditNoteDto>>.Failure("Erreur lors de la récupération des avoirs.");
        }
    }
}
