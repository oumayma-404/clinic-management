using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Invoices.Queries;

/// <summary>Aggregate revenue over a period: invoiced / collected / outstanding. Cancelled invoices excluded.</summary>
public class GetInvoiceRevenueQuery : IRequest<Result<InvoiceRevenueDto>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class GetInvoiceRevenueQueryHandler : IRequestHandler<GetInvoiceRevenueQuery, Result<InvoiceRevenueDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetInvoiceRevenueQueryHandler> _logger;

    public GetInvoiceRevenueQueryHandler(
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetInvoiceRevenueQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _creditNoteRepository = creditNoteRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<InvoiceRevenueDto>> Handle(GetInvoiceRevenueQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<InvoiceRevenueDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            // Unpaged: these are period totals, so every invoice in the window has to be summed. A page would
            // report the revenue of 25 invoices as the revenue of the month.
            var invoices = (await _invoiceRepository.GetFilteredAsync(
                clinicId, request.From, request.To, cancellationToken: cancellationToken)).Items;

            // Only issued (numbered) invoices count; drafts carry no number and cancelled ones are excluded
            // from invoiced/outstanding.
            var billable = invoices
                .Where(i => i.Status != InvoiceStatus.Draft && i.Status != InvoiceStatus.Cancelled)
                .ToList();

            var totalInvoiced = billable.Sum(i => i.TotalTtc);
            var outstanding = billable.Sum(i => i.Outstanding);

            // « Total encaissé » is attributed by payment date (PaidOn) — matching the caisse — not by the
            // invoice issue date (finding #18), so a payment collected in a different period from issuance
            // lands in the same bucket both views use. With no period, fall back to collected-to-date.
            decimal totalCollected;
            if (request.From.HasValue && request.To.HasValue)
            {
                var collected = await _invoiceRepository.GetCollectedBetweenAsync(
                    clinicId, request.From.Value, request.To.Value, cancellationToken);
                // Net out avoirs refunded in the same window so "encaissé" matches the caisse (finding #8).
                var refunds = await _creditNoteRepository.GetRefundedBetweenAsync(
                    clinicId, request.From.Value, request.To.Value, cancellationToken);
                totalCollected = collected - refunds;
            }
            else
            {
                // This branch is what /factures loads on arrival — both date filters start empty — so it is
                // the « Total encaissé » nearly every user actually sees, and it was the one branch that did
                // not net avoirs. A refunded invoice inflated the headline figure indefinitely while the
                // caisse, which has always netted, disagreed with it.
                var creditedByInvoice = await _creditNoteRepository.GetTotalsForInvoicesAsync(
                    billable.Select(i => i.Id).ToList(), cancellationToken);
                totalCollected = billable.Sum(i => i.AmountCollected) - creditedByInvoice.Values.Sum();
            }

            var dto = new InvoiceRevenueDto
            {
                TotalInvoiced = totalInvoiced,
                TotalCollected = totalCollected,
                Outstanding = outstanding
            };

            return Result<InvoiceRevenueDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error computing invoice revenue");
            return Result<InvoiceRevenueDto>.Failure("Erreur lors du calcul des recettes.");
        }
    }
}
