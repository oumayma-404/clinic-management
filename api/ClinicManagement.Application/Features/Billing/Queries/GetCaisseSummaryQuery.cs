using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Billing.Queries;

/// <summary>
/// The caisse (daily cash) summary for the clinic over [From, To]: encaissements (collected invoice
/// payments) minus dépenses (recorded expenses), and the net. Both endpoints default to the current
/// <b>clinic-local</b> day when omitted (AC-P6.3, via <see cref="ClinicClock.TodayRangeUtc"/>).
/// Clinic-scoped; all figures rounded to the millime.
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

            // Default to the current CLINIC-LOCAL day when no range is supplied (AC-P6.3). It used to default to
            // `DateTime.UtcNow.Date`, which runs « aujourd'hui » from 01:00 to 01:00 Tunis (§ 4.1) — the browser
            // callers always sent their own bounds, so the defect only ever bit a direct API caller, but it is a
            // trap sitting in the one read the clinic reconciles its till against.
            //
            // The upper bound is the last tick of the local day, not the next midnight: the "between" queries are
            // inclusive on both ends, so a payment recorded at exactly 00:00 the next day would otherwise be
            // counted in BOTH days (#20). `ClinicClock.TodayRangeUtc` is the single authority for both bounds.
            var (todayFrom, todayToInclusive) = ClinicClock.TodayRangeUtc();
            var from = request.From ?? todayFrom;
            // A supplied `From` with no `To` still means "the 24 hours from there", unchanged — only the
            // no-arguments default moves off UTC.
            var to = request.To ?? (request.From.HasValue ? from.AddDays(1).AddTicks(-1) : todayToInclusive);
            if (to <= from)
                return Result<CaisseSummaryDto>.Failure("La date de fin doit être postérieure à la date de début.");

            // Encaissements = invoice payments + treatment-plan installment collections (both money tracks),
            // so the daily caisse agrees with the dashboard "encaissé" figure (which sums both). The plan
            // side now skips Draft/Cancelled plans (PlanBillingRules, applied in the repository) so an
            // unaccepted devis's échéancier never shows up as clinic cash.
            //
            // Billed-plan de-duplication now applies to cash too — a reversal of the previous rule. It used to
            // say the opposite, and correctly so: the bridge carried no payment onto the invoice, so excluding
            // a bridged plan would have erased real receipts from the till. The bridge now carries that money
            // across at issue, so those receipts live on the invoice track and counting the plan as well would
            // double them.
            var billedPlanIds = PlanBillingRules.BilledPlanIds(
                await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));

            var invoiceCollected = await _invoiceRepository.GetCollectedBetweenAsync(clinicId, from, to, cancellationToken: cancellationToken);
            var installmentCollected = await _planRepository.GetInstallmentCollectedBetweenAsync(
                clinicId, from, to, billedPlanIds, cancellationToken);
            // Avoirs (credit notes) refunded in the period are money OUT and are reported on their own line.
            // They used to be netted into CashIn — correct arithmetically, and wrong the moment the caisse gained
            // an « extrait »: a statement shows a refund leaving the till, so a CashIn that had already absorbed
            // it meant the lines could not sum to the total above them. CashIn is now gross.
            var refunds = await _creditNoteRepository.GetRefundedBetweenAsync(clinicId, from, to, cancellationToken);
            var cashIn = invoiceCollected + installmentCollected;
            var cashOut = await _expenseRepository.GetTotalBetweenAsync(clinicId, from, to, cancellationToken);

            // The « dont espèces » split (L8 slice B). Two GROUP BY reads, one per ledger, each
            // predicate-for-predicate identical to the SUM above it — which is what makes Σ breakdown == CashIn a
            // property rather than a coincidence. Summing the « extrait »'s movement rows instead would have been
            // one read fewer and wrong: those carry voided payments, which the totals drop.
            var byMethod = MergeMethodTotals(
                await _invoiceRepository.GetCollectedByMethodBetweenAsync(clinicId, from, to, cancellationToken),
                await _planRepository.GetInstallmentCollectedByMethodBetweenAsync(
                    clinicId, from, to, billedPlanIds, cancellationToken));

            var dto = new CaisseSummaryDto
            {
                FromDate = from,
                ToDate = to,
                CashIn = InvoiceCalculator.RoundMoney(cashIn),
                Refunds = InvoiceCalculator.RoundMoney(refunds),
                CashOut = InvoiceCalculator.RoundMoney(cashOut),
                Net = InvoiceCalculator.RoundMoney(cashIn - refunds - cashOut),
                CashInByMethod = byMethod
            };

            return Result<CaisseSummaryDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error building the caisse summary");
            return Result<CaisseSummaryDto>.Failure("Erreur lors du calcul de la caisse.");
        }
    }

    /// <summary>
    /// Merges the two ledgers' per-method totals into one line per method, over <b>every</b> declared
    /// <c>PaymentMethod</c> in enum order.
    /// <para>
    /// Enumerating the enum rather than the returned rows is the deliberate part: a repository returns only the
    /// methods present in the window, so a day of cheques alone would drop « Espèces » from the screen — the one
    /// figure the person closing the till is looking for. A method with no receipts reads « 0,000 », which is a
    /// true statement about the drawer; an absent row is not a statement at all.
    /// </para>
    /// <para>
    /// It also means a method added to the enum appears here with no edit, and cannot be silently omitted from a
    /// breakdown the total is supposed to equal.
    /// </para>
    /// </summary>
    private static List<CaisseMethodTotalDto> MergeMethodTotals(
        IReadOnlyList<PaymentMethodTotal> invoiceTotals,
        IReadOnlyList<PaymentMethodTotal> installmentTotals)
    {
        return Enum.GetValues<PaymentMethod>()
            .Select(method => new CaisseMethodTotalDto
            {
                Method = method.ToString(),
                Label = PaymentMethodLabels.ToFrench(method),
                Amount = InvoiceCalculator.RoundMoney(
                    invoiceTotals.Where(t => t.Method == method).Sum(t => t.Amount)
                    + installmentTotals.Where(t => t.Method == method).Sum(t => t.Amount))
            })
            .ToList();
    }
}
