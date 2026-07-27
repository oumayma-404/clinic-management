using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class TreatmentPlanRepository : ITreatmentPlanRepository
{
    private readonly ApplicationDbContext _context;

    public TreatmentPlanRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TreatmentPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TreatmentPlans
            .Include(p => p.Items)
            .Include(p => p.Installments)
            .ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<TreatmentPlan>> GetFilteredAsync(
        Guid clinicId,
        Guid? patientId = null,
        TreatmentPlanStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TreatmentPlans
            .Include(p => p.Items)
            .Include(p => p.Installments)
            .ThenInclude(i => i.Payments)
            .Where(p => p.ClinicId == clinicId);

        if (patientId.HasValue)
        {
            query = query.Where(p => p.PatientId == patientId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(p => p.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(p => p.CreatedAt <= to.Value);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetMaxSequenceForYearAsync(Guid clinicId, int year, CancellationToken cancellationToken = default)
    {
        var prefix = $"{year}-";

        var numbers = await _context.TreatmentPlans
            .Where(p => p.ClinicId == clinicId && p.Number != null && p.Number.StartsWith(prefix))
            .Select(p => p.Number!)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var dashIndex = number.LastIndexOf('-');
            if (dashIndex >= 0 && int.TryParse(number[(dashIndex + 1)..], out var sequence) && sequence > max)
            {
                max = sequence;
            }
        }

        return max;
    }

    public async Task<decimal> GetInstallmentCollectedBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime to,
        IReadOnlyCollection<Guid> excludedPlanIds,
        CancellationToken cancellationToken = default)
    {
        // Committed plans only (PlanBillingRules): a Draft devis's hand-built échéancier is not clinic money
        // and a cancelled plan is void.
        //
        // Bridged plans ARE excluded here — and that is a deliberate reversal of the previous rule. It used to
        // say the opposite, because the bridge copied no payment onto the invoice, so excluding a bridged plan
        // would have deleted real receipts from the caisse. The bridge now carries that money onto the invoice
        // at issue, so those receipts live on the invoice track and counting them here too would double them.
        // This is the condition DEV-5 of treatment-plan-workspace anticipated: "if the carry-over fix ever
        // lands, a de-dup on the collected side becomes necessary AT THAT MOMENT".
        //
        // The exclusion is purely read-side and self-correcting: a Draft bridge does not exclude (the money is
        // still only on the plan), and cancelling the bridge hands the plan straight back.
        var debtStatuses = PlanBillingRules.DebtBearingPlanStatuses;
        var excluded = excludedPlanIds as ICollection<Guid> ?? excludedPlanIds.ToList();

        // Summed from the payment LEDGER, each row on its own date. This used to key the whole cumulative
        // AmountPaid off the single LastPaidOn, so an échéance paid 400 DT in January and 600 in February
        // reported 0 for January and 1000 for February — and January's already-published figure changed
        // retroactively the moment the February payment landed. Mirrors the invoice side, which was always
        // event-sourced and correct.
        //
        // Still rooted at the clinic-filtered TreatmentPlans set and reached by SelectMany: that traversal IS
        // the tenant scoping for a grandchild that has no ClinicId and no DbSet of its own.
        return await _context.TreatmentPlans
            .Where(p => p.ClinicId == clinicId
                        && debtStatuses.Contains(p.Status)
                        && !excluded.Contains(p.Id))
            .SelectMany(p => p.Installments)
            .SelectMany(i => i.Payments)
            .Where(p => !p.IsVoided && p.PaidOn >= from && p.PaidOn <= to)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<(Guid PatientId, decimal Outstanding, DateTime? OldestOverdueDueDate)>> GetInstallmentOutstandingByPatientAsync(
        Guid clinicId, DateTime asOfUtc, IReadOnlyCollection<Guid> excludedPlanIds, CancellationToken cancellationToken = default)
    {
        // Only committed plans carry debt, and a plan already represented by an invoice is counted through
        // that invoice instead — both rules come from PlanBillingRules so « Créances », the dashboard and
        // « Solde patient » can't drift apart.
        var debtStatuses = PlanBillingRules.DebtBearingPlanStatuses;

        var plans = _context.TreatmentPlans
            .Where(p => p.ClinicId == clinicId && debtStatuses.Contains(p.Status));

        if (excludedPlanIds.Count > 0)
        {
            plans = plans.Where(p => !excludedPlanIds.Contains(p.Id));
        }

        // Flatten each remaining plan's not-fully-paid installments (carrying the plan's patient id), then
        // aggregate per patient in memory — a clinic's open-installment set is small, and Amount/AmountPaid
        // arithmetic + a conditional min are clearer this way than as a grouped SQL projection.
        var rows = await plans
            .SelectMany(p => p.Installments
                .Where(i => i.Amount > i.AmountPaid)
                .Select(i => new { p.PatientId, i.Amount, i.AmountPaid, i.DueDate }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.PatientId)
            .Select(g =>
            {
                var outstanding = g.Sum(r => r.Amount - r.AmountPaid);
                // Calendar-day comparison (in memory, so .Date is safe): an échéance due TODAY is not late.
                // Comparing instants against a midnight due date flagged it a full day early.
                var overdueDates = g.Where(r => r.DueDate.Date < asOfUtc.Date).Select(r => r.DueDate).ToList();
                DateTime? oldestOverdue = overdueDates.Count > 0 ? overdueDates.Min() : null;
                return (PatientId: g.Key, Outstanding: outstanding, OldestOverdueDueDate: oldestOverdue);
            })
            .Where(r => r.Outstanding > 0m)
            .ToList();
    }

    public async Task<TreatmentPlan> AddAsync(TreatmentPlan plan, CancellationToken cancellationToken = default)
    {
        await _context.TreatmentPlans.AddAsync(plan, cancellationToken);
        return plan;
    }

    public Task UpdateAsync(TreatmentPlan plan, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(plan);
        if (entry.State == EntityState.Detached)
        {
            _context.TreatmentPlans.Update(plan);
        }
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _context.TreatmentPlans
            .Include(p => p.Items)
            .Include(p => p.Installments)
            .ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (plan != null)
        {
            _context.TreatmentPlans.Remove(plan);
        }
    }
}
