using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Reads the reconciliation facts straight off the DbContext, across every clinic.
///
/// The cross-clinic read needs no <c>IgnoreQueryFilters()</c>: the <c>reconcile-money</c> console verb builds its
/// container from <c>AddInfrastructure</c> alone, so no <c>ICurrentClinicProvider</c> is registered, the context's
/// optional provider is null, and every global clinic filter is inactive.
///
/// Deliberately read-only — it never calls <c>SaveChanges</c> and stages no entity. Projections are materialised
/// and aggregated in memory rather than pushed into grouped SQL: this runs once, by an operator, on a stopped
/// app, and legibility matters more than a round trip.
/// </summary>
public class MoneyReconciliationReader : IMoneyReconciliationReader
{
    private readonly ApplicationDbContext _context;

    public MoneyReconciliationReader(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MoneyReconciliationFacts> ReadAsync(
        int monthsOfHistory,
        CancellationToken cancellationToken = default)
    {
        var since = MonthStartUtc(DateTime.UtcNow).AddMonths(-Math.Max(0, monthsOfHistory - 1));
        var debtBearing = PlanBillingRules.DebtBearingPlanStatuses.ToArray();

        var clinics = await _context.Clinics
            .AsNoTracking()
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        // Cancelled invoices are excluded everywhere below for the same reason the caisse excludes them.
        var invoices = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Cancelled)
            .Select(i => new
            {
                i.ClinicId,
                i.Id,
                i.Number,
                i.AmountCollected,
                i.TreatmentPlanId
            })
            .ToListAsync(cancellationToken);

        var payments = await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Cancelled)
            .SelectMany(i => i.Payments.Select(p => new { i.ClinicId, p.Amount, p.PaidOn }))
            .ToListAsync(cancellationToken);

        var plans = await _context.TreatmentPlans
            .AsNoTracking()
            .Where(p => debtBearing.Contains(p.Status))
            .Select(p => new { p.ClinicId, p.Id, p.Number, p.TotalPlanned })
            .ToListAsync(cancellationToken);

        var installments = await _context.TreatmentPlans
            .AsNoTracking()
            .Where(p => debtBearing.Contains(p.Status))
            .SelectMany(p => p.Installments.Select(i => new
            {
                p.ClinicId,
                PlanId = p.Id,
                i.Amount,
                i.AmountPaid,
                i.LastPaidOn
            }))
            .ToListAsync(cancellationToken);

        var ledger = await _context.TreatmentPlans
            .AsNoTracking()
            .Where(p => debtBearing.Contains(p.Status))
            .SelectMany(p => p.Installments.SelectMany(i => i.Payments.Select(pay => new
            {
                p.ClinicId,
                PlanId = p.Id,
                pay.Amount,
                pay.PaidOn,
                pay.IsVoided
            })))
            .ToListAsync(cancellationToken);

        var contacts = await _context.Patients
            .AsNoTracking()
            .Select(p => new { p.ClinicId, Email = p.Email.Value, Phone = p.PhoneNumber.Value })
            .ToListAsync(cancellationToken);

        var creditNotes = await _context.CreditNotes
            .AsNoTracking()
            .Select(c => new { c.ClinicId, c.InvoiceId, c.Amount })
            .ToListAsync(cancellationToken);

        var orphans = new OrphanFacts(
            Invoices: await _context.Invoices
                .CountAsync(i => !_context.Patients.Any(p => p.Id == i.PatientId), cancellationToken),
            TreatmentPlans: await _context.TreatmentPlans
                .CountAsync(t => !_context.Patients.Any(p => p.Id == t.PatientId), cancellationToken),
            ToothStates: await _context.ToothStates
                .CountAsync(t => !_context.Patients.Any(p => p.Id == t.PatientId), cancellationToken),
            Notifications: await _context.Notifications
                .CountAsync(n => n.PatientId != null && !_context.Patients.Any(p => p.Id == n.PatientId), cancellationToken));

        // Invoices that already hold at least one carried-over payment.
        var carriedInvoiceIds = (await _context.Invoices
            .AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Cancelled)
            .SelectMany(i => i.Payments
                .Where(p => p.SourceInstallmentPaymentId != null)
                .Select(p => i.Id))
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var creditedByInvoice = creditNotes
            .GroupBy(c => c.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        var perClinic = clinics.Select(clinic =>
        {
            var clinicInvoices = invoices.Where(i => i.ClinicId == clinic.Id).ToList();
            var clinicPayments = payments.Where(p => p.ClinicId == clinic.Id).ToList();
            var clinicInstallments = installments.Where(i => i.ClinicId == clinic.Id).ToList();
            var clinicLedger = ledger.Where(l => l.ClinicId == clinic.Id && !l.IsVoided).ToList();
            var clinicCarriedInvoiceIds = carriedInvoiceIds;

            var planSchedules = plans
                .Where(p => p.ClinicId == clinic.Id)
                .Select(p => new PlanScheduleFact(
                    p.Id,
                    p.Number,
                    p.TotalPlanned,
                    clinicInstallments.Where(i => i.PlanId == p.Id).Sum(i => i.Amount)))
                .ToList();

            var overCredited = clinicInvoices
                .Where(i => creditedByInvoice.ContainsKey(i.Id)
                            && InvoiceCalculator.RoundMoney(creditedByInvoice[i.Id])
                               > InvoiceCalculator.RoundMoney(i.AmountCollected))
                .Select(i => new OverCreditedInvoiceFact(i.Id, i.Number, i.AmountCollected, creditedByInvoice[i.Id]))
                .ToList();

            var duplicateBridges = clinicInvoices
                .Where(i => i.TreatmentPlanId != null)
                .GroupBy(i => i.TreatmentPlanId!.Value)
                .Where(g => g.Count() > 1)
                .Select(g => new DuplicateBridgeFact(
                    g.Key,
                    plans.FirstOrDefault(p => p.Id == g.Key)?.Number,
                    g.Count()))
                .ToList();

            // Bridge invoices whose devis had collected money that was never carried across. A carried payment
            // records SourceInstallmentPaymentId, so an invoice with none while its plan holds live payments
            // predates the carry-over.
            var untransferred = clinicInvoices
                .Where(i => i.TreatmentPlanId != null)
                .Select(i => new
                {
                    Invoice = i,
                    CollectedOnPlan = ledger
                        .Where(l => !l.IsVoided && l.PlanId == i.TreatmentPlanId!.Value)
                        .Sum(l => l.Amount),
                    HasCarried = clinicCarriedInvoiceIds.Contains(i.Id)
                })
                .Where(x => x.CollectedOnPlan > 0m && !x.HasCarried)
                .Select(x => new UntransferredBridgeFact(
                    x.Invoice.Id,
                    x.Invoice.Number,
                    plans.FirstOrDefault(p => p.Id == x.Invoice.TreatmentPlanId!.Value)?.Number,
                    x.CollectedOnPlan))
                .ToList();

            return new ClinicMoneyFacts(
                clinic.Id,
                clinic.Name,
                PaymentRowSum: clinicPayments.Sum(p => p.Amount),
                InvoiceAmountCollectedSum: clinicInvoices.Sum(i => i.AmountCollected),
                InstallmentAmountPaidSum: clinicInstallments.Sum(i => i.AmountPaid),
                InstallmentLedgerSum: clinicLedger.Sum(l => l.Amount),
                PlanSchedules: planSchedules,
                MonthlyCollected: BuildMonthlyCollected(
                    clinicPayments
                        .Where(p => p.PaidOn >= since)
                        .Select(p => (p.PaidOn, p.Amount)),
                    // The ledger figure — what the caisse now reports.
                    clinicLedger
                        .Where(l => l.PaidOn >= since)
                        .Select(l => (l.PaidOn, l.Amount)),
                    // The same month the OLD way, so a before/after run can prove nothing moved (AC-24).
                    clinicInstallments
                        .Where(i => i.LastPaidOn != null && i.LastPaidOn >= since)
                        .Select(i => (i.LastPaidOn!.Value, i.AmountPaid))),
                ContactValues: contacts
                    .Where(c => c.ClinicId == clinic.Id)
                    .Select(c => new ContactValueFact(c.Email, c.Phone))
                    .ToList(),
                OverCreditedInvoices: overCredited,
                DuplicateBridges: duplicateBridges,
                UntransferredBridges: untransferred);
        }).ToList();

        return new MoneyReconciliationFacts(perClinic, orphans);
    }

    /// <summary>
    /// Buckets both cash tracks by calendar month. The installment side attributes the <b>whole cumulative</b>
    /// <c>AmountPaid</c> to the single <c>LastPaidOn</c> — reproducing exactly what the caisse and the dashboard
    /// report today, which is the point: this is the baseline the ledger migration must not move.
    /// </summary>
    private static List<MonthlyCollectedFact> BuildMonthlyCollected(
        IEnumerable<(DateTime PaidOn, decimal Amount)> invoicePayments,
        IEnumerable<(DateTime PaidOn, decimal Amount)> ledgerPayments,
        IEnumerable<(DateTime PaidOn, decimal Amount)> legacyInstallmentPayments)
    {
        static Dictionary<(int Year, int Month), decimal> ByMonth(IEnumerable<(DateTime PaidOn, decimal Amount)> rows) =>
            rows.GroupBy(p => (p.PaidOn.Year, p.PaidOn.Month))
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var invoiceByMonth = ByMonth(invoicePayments);
        var ledgerByMonth = ByMonth(ledgerPayments);
        var legacyByMonth = ByMonth(legacyInstallmentPayments);

        return invoiceByMonth.Keys
            .Union(ledgerByMonth.Keys)
            .Union(legacyByMonth.Keys)
            .OrderBy(k => k.Year).ThenBy(k => k.Month)
            .Select(k => new MonthlyCollectedFact(
                k.Year,
                k.Month,
                invoiceByMonth.TryGetValue(k, out var invoice) ? invoice : 0m,
                ledgerByMonth.TryGetValue(k, out var ledgerAmount) ? ledgerAmount : 0m,
                legacyByMonth.TryGetValue(k, out var legacy) ? legacy : 0m))
            .ToList();
    }

    private static DateTime MonthStartUtc(DateTime moment) =>
        new(moment.Year, moment.Month, 1, 0, 0, 0, DateTimeKind.Utc);
}
