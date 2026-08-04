using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

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
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetInvoiceRevenueQueryHandler> _logger;

    public GetInvoiceRevenueQueryHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        ICreditNoteRepository creditNoteRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetInvoiceRevenueQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
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

            /*
             * « Total encaissé » is the clinic's third money read, and until J5 it counted **invoice payments
             * only** while la caisse and the dashboard both added devis instalments. So a practice collecting on
             * an échéancier saw a smaller figure here than on the two screens beside it, with nothing to explain
             * the gap — and `MoneyReadConsistencyTests`, which pins caisse↔dashboard, never touched this read,
             * which is precisely how the divergence survived.
             *
             * The plan side goes through the same `PlanBillingRules.BilledPlanIds` de-dup the other two use: a
             * devis bridged into a real invoice has its collections carried onto the invoice track, so counting
             * the plan as well would double them. One projection, read once, shared by both branches below.
             */
            var billedPlanIds = PlanBillingRules.BilledPlanIds(
                await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));

            // Attributed by payment date (PaidOn) — matching the caisse — not by the invoice issue date
            // (finding #18), so a payment collected in a different period from issuance lands in the same bucket
            // both views use. With no period, fall back to collected-to-date.
            decimal totalCollected;
            if (request.From.HasValue && request.To.HasValue)
            {
                var collected = await _invoiceRepository.GetCollectedBetweenAsync(
                    clinicId, request.From.Value, request.To.Value, cancellationToken: cancellationToken);
                var planCollected = await _planRepository.GetInstallmentCollectedBetweenAsync(
                    clinicId, request.From.Value, request.To.Value, billedPlanIds, cancellationToken);
                // Net out avoirs refunded in the same window so "encaissé" matches the caisse (finding #8).
                var refunds = await _creditNoteRepository.GetRefundedBetweenAsync(
                    clinicId, request.From.Value, request.To.Value, cancellationToken);
                totalCollected = collected + planCollected - refunds;
            }
            else
            {
                // This branch is what /factures loads on arrival — both date filters start empty — so it is
                // the « Total encaissé » nearly every user actually sees, and it was the one branch that did
                // not net avoirs. A refunded invoice inflated the headline figure indefinitely while the
                // caisse, which has always netted, disagreed with it.
                //
                // The plan side has no date-free aggregate, and inventing one would be a second predicate over
                // the same rows — the class of duplication this whole spec is about. « Depuis toujours » is
                // honestly expressed as the whole time axis, so the windowed call is asked for exactly that.
                var creditedByInvoice = await _creditNoteRepository.GetTotalsForInvoicesAsync(
                    billable.Select(i => i.Id).ToList(), cancellationToken);
                var planCollected = await _planRepository.GetInstallmentCollectedBetweenAsync(
                    clinicId, DateTime.MinValue, DateTime.MaxValue, billedPlanIds, cancellationToken);
                totalCollected = billable.Sum(i => i.AmountCollected) + planCollected - creditedByInvoice.Values.Sum();
            }

            var dto = new InvoiceRevenueDto
            {
                TotalInvoiced = InvoiceCalculator.RoundMoney(totalInvoiced),
                // Rounded through the one arithmetic authority — this was the only money read that did not, so
                // a sum of two ledgers could print a fourth decimal the rest of the product never shows.
                TotalCollected = InvoiceCalculator.RoundMoney(totalCollected),
                Outstanding = InvoiceCalculator.RoundMoney(outstanding)
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
