using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Dashboard.Readers;

/// <summary>
/// Cash over the period and the one before it — encaissé, facturé, dépenses, net — plus the live créances total.
///
/// <para><b>This class must agree with la caisse.</b> It deliberately calls the <i>same</i> repository methods
/// <c>GetCaisseSummaryQuery</c> calls, in the same order, with the same billed-plan exclusion, so the same window
/// over the same data yields the same three figures. Reimplementing any part of the arithmetic here — even
/// "equivalently" — is how the dashboard and the caisse came to report different cash for the same month before, and
/// <c>MoneyReadConsistencyTests</c> exists to catch a regression of exactly that.</para>
///
/// <para><b>Why the billed-plan set is computed once.</b> <c>PlanBillingRules</c> is the single de-duplication
/// authority: a devis bridged into a real invoice has its collected money carried onto that invoice at issue, so
/// counting the plan's échéancier as well doubles the cash, and counting its balance as well doubles the debt. Both
/// the cash and the debt call therefore receive the same set, computed from one read.</para>
/// </summary>
public class DashboardMoneyReader : IDashboardMoneyReader
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;

    public DashboardMoneyReader(
        IInvoiceRepository invoiceRepository,
        ITreatmentPlanRepository planRepository,
        IExpenseRepository expenseRepository,
        ICreditNoteRepository creditNoteRepository)
    {
        _invoiceRepository = invoiceRepository;
        _planRepository = planRepository;
        _expenseRepository = expenseRepository;
        _creditNoteRepository = creditNoteRepository;
    }

    public async Task<(DashboardMoneyDto Money, DashboardReceivablesDto Receivables)> ReadAsync(
        Guid clinicId, DashboardPeriod period, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // One read, used by four calls below (two cash windows + the debt side). The links projection is light by
        // design — no lines, no payments.
        var billedPlanIds = PlanBillingRules.BilledPlanIds(
            await _invoiceRepository.GetTreatmentPlanLinksAsync(clinicId, cancellationToken));

        var collected = await CollectedAsync(clinicId, period.From, period.ToInclusive, billedPlanIds, cancellationToken);
        var previousCollected = await CollectedAsync(
            clinicId, period.PreviousFrom, period.PreviousToInclusive, billedPlanIds, cancellationToken);

        var refunds = await _creditNoteRepository.GetRefundedBetweenAsync(
            clinicId, period.From, period.ToInclusive, cancellationToken);
        var previousRefunds = await _creditNoteRepository.GetRefundedBetweenAsync(
            clinicId, period.PreviousFrom, period.PreviousToInclusive, cancellationToken);

        var invoiced = await _invoiceRepository.GetInvoicedBetweenAsync(
            clinicId, period.From, period.ToInclusive, cancellationToken);
        var previousInvoiced = await _invoiceRepository.GetInvoicedBetweenAsync(
            clinicId, period.PreviousFrom, period.PreviousToInclusive, cancellationToken);

        var expenses = await _expenseRepository.GetTotalBetweenAsync(
            clinicId, period.From, period.ToInclusive, cancellationToken);
        var previousExpenses = await _expenseRepository.GetTotalBetweenAsync(
            clinicId, period.PreviousFrom, period.PreviousToInclusive, cancellationToken);

        var money = new DashboardMoneyDto
        {
            Collected = PeriodComparison.Of(collected, previousCollected),
            Invoiced = PeriodComparison.Of(Round(invoiced), Round(previousInvoiced)),
            Refunds = PeriodComparison.Of(Round(refunds), Round(previousRefunds)),
            Expenses = PeriodComparison.Of(Round(expenses), Round(previousExpenses)),
            // Net is derived from the already-rounded parts rather than rounded again from raw sums, so
            // Collected − Refunds − Expenses = Net holds exactly as displayed. A caisse that does not add up is a
            // caisse nobody trusts, even when every individual figure is right.
            Net = PeriodComparison.Of(
                Round(collected - Round(refunds) - Round(expenses)),
                Round(previousCollected - Round(previousRefunds) - Round(previousExpenses)))
        };

        var receivables = new DashboardReceivablesDto
        {
            Total = await OutstandingAsync(clinicId, nowUtc, billedPlanIds, cancellationToken)
        };

        return (money, receivables);
    }

    /// <summary>
    /// « Encaissé » over a window: invoice payments + treatment-plan installment collections, <b>gross</b>.
    /// Identical composition to <c>GetCaisseSummaryQuery</c>'s <c>CashIn</c> — refunds are read separately by the
    /// caller and reported on their own figure, because the caisse statement shows a refund as money leaving.
    /// </summary>
    private async Task<decimal> CollectedAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        IReadOnlyCollection<Guid> billedPlanIds,
        CancellationToken cancellationToken)
    {
        var invoiceCollected = await _invoiceRepository.GetCollectedBetweenAsync(
            clinicId, from, toInclusive, cancellationToken);
        var installmentCollected = await _planRepository.GetInstallmentCollectedBetweenAsync(
            clinicId, from, toInclusive, billedPlanIds, cancellationToken);

        return Round(invoiceCollected + installmentCollected);
    }

    /// <summary>
    /// Clinic-wide outstanding across both money tracks, de-duplicated by the same rule « Solde patient » and
    /// « Créances » apply — so the dashboard cannot overstate what patients owe.
    /// </summary>
    private async Task<decimal> OutstandingAsync(
        Guid clinicId,
        DateTime nowUtc,
        IReadOnlyCollection<Guid> billedPlanIds,
        CancellationToken cancellationToken)
    {
        var invoiceOutstanding = (await _invoiceRepository.GetOutstandingByPatientAsync(clinicId, cancellationToken))
            .Sum(r => r.Outstanding);
        var installmentOutstanding = (await _planRepository.GetInstallmentOutstandingByPatientAsync(
                clinicId, nowUtc, billedPlanIds, cancellationToken))
            .Sum(r => r.Outstanding);

        return Round(invoiceOutstanding + installmentOutstanding);
    }

    /// <summary>Every figure leaves this class rounded through the single money authority (millime, away-from-zero).</summary>
    private static decimal Round(decimal value) => InvoiceCalculator.RoundMoney(value);
}
