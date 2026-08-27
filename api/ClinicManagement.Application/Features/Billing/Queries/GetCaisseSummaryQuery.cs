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
    /// <summary>
    /// The window as bare clinic-local calendar days (<c>YYYY-MM-DD</c>) — what every screen sends, and what makes
    /// « la caisse du 3 août » mean the <b>Tunisian</b> 3 August whatever timezone the workstation is set to
    /// (AC-6). <see cref="CaissePeriod"/> resolves them; <c>ToDay</c> defaults to <c>FromDay</c>.
    /// </summary>
    public string? FromDay { get; set; }

    /// <inheritdoc cref="FromDay"/>
    public string? ToDay { get; set; }

    /// <summary>
    /// The window as explicit instants. Kept for callers that genuinely have one (a job, an export driven by
    /// another read's bounds); the day keys above win when both are supplied.
    /// </summary>
    public DateTime? From { get; set; }

    /// <inheritdoc cref="From"/>
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

            // Every bound this read uses comes from CaissePeriod — the same type the « extrait » resolves with, so
            // the statement can never describe a different period from the totals above it (they used to hold two
            // byte-identical copies of this arithmetic, kept in step by a comment).
            var period = CaissePeriod.Resolve(request.FromDay, request.ToDay, request.From, request.To);
            if (period.IsFailure)
                return Result<CaisseSummaryDto>.FailureFrom(period);
            var (from, to) = (period.Value!.From, period.Value.To);

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
