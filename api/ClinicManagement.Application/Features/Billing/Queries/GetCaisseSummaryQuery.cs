using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>
/// The caisse (daily cash) summary for the clinic over [From, To): encaissements (collected invoice
/// payments) minus dépenses (recorded expenses), and the net. Both endpoints default to the current UTC
/// day when omitted. Clinic-scoped; all figures rounded to the millime.
/// </summary>
public class GetCaisseSummaryQuery : IRequest<Result<CaisseSummaryDto>>
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class GetCaisseSummaryQueryHandler : IRequestHandler<GetCaisseSummaryQuery, Result<CaisseSummaryDto>>
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly ILogger<GetCaisseSummaryQueryHandler> _logger;

    public GetCaisseSummaryQueryHandler(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IExpenseRepository expenseRepository,
        ICreditNoteRepository creditNoteRepository,
        ICurrentClinicResolver clinicResolver,
        ILogger<GetCaisseSummaryQueryHandler> logger)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _expenseRepository = expenseRepository;
        _creditNoteRepository = creditNoteRepository;
        _clinicResolver = clinicResolver;
        _logger = logger;
    }

    public async Task<Result<CaisseSummaryDto>> Handle(GetCaisseSummaryQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
                return Result<CaisseSummaryDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            var clinicId = clinicResult.Value;

            // Default to the current UTC day when no range is supplied. The default upper bound is the last
            // tick of the day (not the next midnight): the "between" queries are inclusive on both ends, so a
            // payment recorded at exactly 00:00 the next day would otherwise be counted in BOTH days (#20).
            var from = request.From ?? DateTime.UtcNow.Date;
            var to = request.To ?? from.AddDays(1).AddTicks(-1);
            if (to <= from)
                return Result<CaisseSummaryDto>.Failure("La date de fin doit être postérieure à la date de début.");

            // Encaissements = invoice payments + treatment-plan installment collections (both money tracks),
            // so the daily caisse agrees with the dashboard "encaissé" figure (which sums both). The plan
            // side now skips Draft/Cancelled plans (PlanBillingRules, applied in the repository) so an
            // unaccepted devis's échéancier never shows up as clinic cash.
            //
            // No billed-plan de-duplication here, unlike the outstanding reads: this is cash *received*, and
            // the devis→facture bridge carries no payment onto the invoice (the bridge invoice starts at
            // AmountCollected = 0). Suppressing a bridged plan's collections would erase real receipts from
            // the till instead of removing a double count.
            var invoiceCollected = await _invoiceRepository.GetCollectedBetweenAsync(clinicId, from, to, cancellationToken);
            var installmentCollected = await _planRepository.GetInstallmentCollectedBetweenAsync(clinicId, from, to, cancellationToken);
            // Avoirs (credit notes) refunded in the period reduce net encaissements (finding #8) — netted into
            // CashIn so the caisse stays reconcilable (CashIn − CashOut = Net) without a new DTO field.
            var refunds = await _creditNoteRepository.GetRefundedBetweenAsync(clinicId, from, to, cancellationToken);
            var cashIn = invoiceCollected + installmentCollected - refunds;
            var cashOut = await _expenseRepository.GetTotalBetweenAsync(clinicId, from, to, cancellationToken);

            var dto = new CaisseSummaryDto
            {
                FromDate = from,
                ToDate = to,
                CashIn = InvoiceCalculator.RoundMoney(cashIn),
                CashOut = InvoiceCalculator.RoundMoney(cashOut),
                Net = InvoiceCalculator.RoundMoney(cashIn - cashOut)
            };

            return Result<CaisseSummaryDto>.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building the caisse summary");
            return Result<CaisseSummaryDto>.Failure("Erreur lors du calcul de la caisse.");
        }
    }
}
