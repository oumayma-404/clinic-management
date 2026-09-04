using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Persistence;

using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Common;
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
                .ThenInclude(i => i.Steps)
            .Include(p => p.Installments)
            .ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<TreatmentPlan>> GetByLinkedDentalRecordAsync(
        Guid clinicId, Guid dentalRecordId, CancellationToken cancellationToken = default)
    {
        // Items are included because the caller un-marks the matching act through the aggregate root; the
        // échéancier is not, since detaching an act never touches money.
        //
        // ⚠️ The steps are matched too, and that is not an optimisation. A stepped act only takes its own
        // LinkedDentalRecordId once its LAST step lands, so a fiche that carried out step 1 of 3 is recorded
        // on the step alone. Matching the act's link only would leave that fiche undiscoverable here, and
        // deleting it would strand a step marked « réalisée » against a record that no longer exists.
        return await _context.TreatmentPlans
            .Include(p => p.Items)
                .ThenInclude(i => i.Steps)
            .Where(p => p.ClinicId == clinicId && p.Items.Any(i =>
                i.LinkedDentalRecordId == dentalRecordId
                || i.Steps.Any(s => s.LinkedDentalRecordId == dentalRecordId)))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<TreatmentInProgressFact>> GetTreatmentsInProgressAsync(
        Guid clinicId, PageRequest? paging, string? searchTerm = null, CancellationToken cancellationToken = default)
    {
        // Matched in SQL over the whole clinic, never over the page — see the interface. Null/blank leaves the
        // list untouched rather than matching nothing.
        var pattern = string.IsNullOrWhiteSpace(searchTerm) ? null : SearchTerm.ToLikePattern(searchTerm);
        // Projected in SQL, one row per act — see the interface for why the list cannot be paged over plans.
        // The status filter is the stored TreatmentPlanItem.Status, which is exactly why that column is stored
        // and recomputed rather than derived on read: a domain property over the step rows has no translation.
        /*
         * ⚠ The ORDER BY is expressed HERE, before `select`, and it must stay here.
         *
         * A query-expression `orderby` clause sorts the joined rows — `item` and `plan` — which is what
         * PostgreSQL can sort. Written after the projection (`query.OrderBy(f => f.LastStepDoneOn)`) it sorts
         * a `TreatmentInProgressFact`, and EF cannot see through a record's constructor to the argument that
         * fed one property: it lifts the WHOLE `new TreatmentInProgressFact(...)` call — every Count and
         * FirstOrDefault subquery with it — into the ORDER BY and throws « The LINQ expression could not be
         * translated ». The handler's catch-all then reports « Erreur lors du chargement des traitements en
         * cours. » as a 400, so the screen shows a load failure and the log holds a 40-line expression tree.
         * Nothing in UnitTests touches a database, so no test can see this: the page is the only witness.
         */
        var query =
            from item in _context.Set<TreatmentPlanItem>()
            join plan in _context.TreatmentPlans on item.TreatmentPlanId equals plan.Id
            where plan.ClinicId == clinicId
                  && (plan.Status == TreatmentPlanStatus.Accepted || plan.Status == TreatmentPlanStatus.InProgress)
                  && item.Status == TreatmentPlanItemStatus.InProgress
                  && (pattern == null
                      || EF.Functions.ILike(SqlSearch.Unaccent(plan.Number)!, pattern, SqlSearch.EscapeString)
                      || _context.Patients.Any(pa =>
                          pa.Id == plan.PatientId
                          && (EF.Functions.ILike(
                                  SqlSearch.Unaccent(pa.FirstName + " " + pa.LastName)!, pattern, SqlSearch.EscapeString)
                              || EF.Functions.ILike(
                                  SqlSearch.Unaccent(pa.LastName + " " + pa.FirstName)!, pattern, SqlSearch.EscapeString))))
            // Most recent devis first, then the act's rank inside it so a plan's acts stay in protocol order.
            // The act's own id last and unique: without it OFFSET can repeat one act and skip another, which
            // reads as « un traitement a disparu ». See the interface for why this is no longer oldest-first.
            orderby plan.CreatedAt descending, item.SequenceNumber, item.Id
            select new TreatmentInProgressFact(
                plan.Id,
                plan.Number,
                plan.PatientId,
                item.Id,
                item.DesignationFr,
                item.SequenceNumber,
                item.Steps.Count,
                item.Steps.Count(s => s.DoneDate != null),
                item.Steps.Where(s => s.DoneDate == null)
                    .OrderBy(s => s.SequenceNumber)
                    .Select(s => (Guid?)s.Id)
                    .FirstOrDefault(),
                item.Steps.Where(s => s.DoneDate == null)
                    .OrderBy(s => s.SequenceNumber)
                    .Select(s => s.Label)
                    .FirstOrDefault(),
                item.Steps.Where(s => s.DoneDate == null)
                    .OrderBy(s => s.SequenceNumber)
                    .Select(s => (int?)s.SequenceNumber)
                    .FirstOrDefault(),
                item.Steps.Where(s => s.DoneDate == null)
                    .OrderBy(s => s.SequenceNumber)
                    .Select(s => s.EstimatedDurationMinutes)
                    .FirstOrDefault(),
                item.Steps.Where(s => s.DoneDate == null)
                    .OrderBy(s => s.SequenceNumber)
                    .Select(s => s.MinDaysAfterPrevious)
                    .FirstOrDefault(),
                item.Steps.Where(s => s.DoneDate != null).Max(s => s.DoneDate));

        var totalCount = await query.CountAsync(cancellationToken);
        if (paging is not { } page)
        {
            return PagedResult<TreatmentInProgressFact>.Unpaged(await query.ToListAsync(cancellationToken));
        }

        var items = await query.Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);
        return new PagedResult<TreatmentInProgressFact>(items, page.Page, page.PageSize, totalCount);
    }

    public async Task<PagedResult<TreatmentPlan>> GetFilteredAsync(
        Guid clinicId,
        Guid? patientId = null,
        TreatmentPlanStatus? status = null,
        DateTime? from = null,
        DateTime? to = null,
        DateTime? acceptedFrom = null,
        DateTime? acceptedTo = null,
        string? searchTerm = null,
        PageRequest? paging = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TreatmentPlans
            .Include(p => p.Items)
                .ThenInclude(i => i.Steps)
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

        // AcceptedDate, not CreatedAt — the dashboard's « Devis acceptés » counts when the patient said yes, and
        // drilling into that figure with the created-date range would list a different set of devis than the card
        // counted. A plan with no AcceptedDate has not been accepted, so it cannot fall in an accepted-date window.
        if (acceptedFrom.HasValue)
        {
            query = query.Where(p => p.AcceptedDate != null && p.AcceptedDate >= acceptedFrom.Value);
        }

        if (acceptedTo.HasValue)
        {
            query = query.Where(p => p.AcceptedDate != null && p.AcceptedDate <= acceptedTo.Value);
        }

        // Devis number, title, notes, or the patient's name. The patient half is an EXISTS for the same reason
        // as on the invoice side: names are resolved by a batched lookup after the page is cut.
        var pattern = SearchTerm.ToLikePattern(searchTerm);
        if (pattern is not null)
        {
            query = query.Where(p =>
                EF.Functions.ILike(SqlSearch.Unaccent(p.Number)!, pattern, SqlSearch.EscapeString)
                || EF.Functions.ILike(SqlSearch.Unaccent(p.Title)!, pattern, SqlSearch.EscapeString)
                || EF.Functions.ILike(SqlSearch.Unaccent(p.Notes)!, pattern, SqlSearch.EscapeString)
                // BOTH name orders: the app renders « Nom Prénom » — see `PatientRepository.ApplySearch`.
                || _context.Patients.Any(pa => pa.Id == p.PatientId
                    && (EF.Functions.ILike(
                            SqlSearch.Unaccent(pa.FirstName + " " + pa.LastName)!, pattern, SqlSearch.EscapeString)
                        || EF.Functions.ILike(
                            SqlSearch.Unaccent(pa.LastName + " " + pa.FirstName)!, pattern, SqlSearch.EscapeString))));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToPagedResultAsync(paging, cancellationToken);
    }

    public async Task<IReadOnlyList<RecallPlanFact>> GetRecallPlanFactsAsync(
        Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Item counts are computed in SQL (two correlated COUNTs), so no TreatmentPlanItem row is materialised.
        // Cancelled is void and Completed has nothing left to do, so neither can put a patient on the worklist.
        var rows = await _context.TreatmentPlans
            .Where(p => p.ClinicId == clinicId
                        && p.Status != TreatmentPlanStatus.Cancelled
                        && p.Status != TreatmentPlanStatus.Completed)
            .Select(p => new
            {
                p.PatientId,
                PlanId = p.Id,
                p.Number,
                p.Status,
                p.CreatedAt,
                p.AcceptedDate,
                TotalItems = p.Items.Count,
                DoneItems = p.Items.Count(i => i.Status == TreatmentPlanItemStatus.Done)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new RecallPlanFact(
                r.PatientId, r.PlanId, r.Number, r.Status, r.CreatedAt, r.AcceptedDate, r.TotalItems, r.DoneItems))
            .ToList();
    }

    public async Task<int> CountByStatusAsync(
        Guid clinicId,
        TreatmentPlanStatus status,
        DateTime? from = null,
        DateTime? toInclusive = null,
        bool byAcceptedDate = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.TreatmentPlans.Where(p => p.ClinicId == clinicId && p.Status == status);

        if (byAcceptedDate)
        {
            if (from.HasValue)
            {
                query = query.Where(p => p.AcceptedDate != null && p.AcceptedDate >= from.Value);
            }

            if (toInclusive.HasValue)
            {
                query = query.Where(p => p.AcceptedDate != null && p.AcceptedDate <= toInclusive.Value);
            }
        }
        else
        {
            if (from.HasValue)
            {
                query = query.Where(p => p.CreatedAt >= from.Value);
            }

            if (toInclusive.HasValue)
            {
                query = query.Where(p => p.CreatedAt <= toInclusive.Value);
            }
        }

        return await query.CountAsync(cancellationToken);
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
        // ⚠️ A Draft bridge does not exclude (the money is still only on the plan). But the exclusion is NOT
        // undone by cancelling the bridge, which this comment used to claim: an invoice holding a non-voided
        // payment cannot be cancelled at all, and a bridge that carried collections is in exactly that state.
        // The avoir is the only correction. See PlanBillingRules for the full note.
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

    public async Task<IReadOnlyList<CaisseInstallmentPaymentRow>> GetInstallmentPaymentsBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        IReadOnlyCollection<Guid> excludedPlanIds,
        CancellationToken cancellationToken = default)
    {
        // Predicate-for-predicate the same as GetInstallmentCollectedBetweenAsync, for the same reason its
        // invoice-side twin mirrors GetCollectedBetweenAsync: the statement must sum to the figure that method
        // returns. Committed plans only, bridged plans excluded (their collections live on the invoice track),
        // same inclusive bounds. The one deliberate difference: voided rows are returned, not filtered.
        //
        // Rooted at the clinic-filtered TreatmentPlans set and reached by SelectMany — that traversal IS the
        // tenant scoping for a great-grandchild with no ClinicId and no DbSet of its own.
        var debtStatuses = PlanBillingRules.DebtBearingPlanStatuses;
        var excluded = excludedPlanIds as ICollection<Guid> ?? excludedPlanIds.ToList();

        var rows = await _context.TreatmentPlans
            .Where(p => p.ClinicId == clinicId
                        && debtStatuses.Contains(p.Status)
                        && !excluded.Contains(p.Id))
            .SelectMany(plan => plan.Installments
                .SelectMany(i => i.Payments
                    .Where(pay => pay.PaidOn >= from && pay.PaidOn <= toInclusive)
                    .Select(pay => new
                    {
                        PaymentId = pay.Id,
                        TreatmentPlanId = plan.Id,
                        InstallmentId = i.Id,
                        PlanNumber = plan.Number,
                        plan.PatientId,
                        pay.Amount,
                        pay.Method,
                        pay.PaidOn,
                        pay.IsVoided,
                        pay.VoidReason,
                        pay.VoidedByName,
                        pay.ChequeNumber,
                        pay.ChequeBankName,
                        pay.ChequeDueDate
                    })))
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CaisseInstallmentPaymentRow(
                r.PaymentId, r.TreatmentPlanId, r.InstallmentId, r.PlanNumber, r.PatientId,
                r.Amount, r.Method, r.PaidOn, r.IsVoided, r.VoidReason, r.VoidedByName,
                r.ChequeNumber, r.ChequeBankName, r.ChequeDueDate))
            .ToList();
    }

    public async Task<IReadOnlyList<PaymentMethodTotal>> GetInstallmentCollectedByMethodBetweenAsync(
        Guid clinicId,
        DateTime from,
        DateTime toInclusive,
        IReadOnlyCollection<Guid> excludedPlanIds,
        CancellationToken cancellationToken = default)
    {
        // GetInstallmentCollectedBetweenAsync with a GROUP BY — identical committed-plan filter, identical
        // bridged-plan exclusion, identical `!IsVoided` and bounds. The breakdown is shown under the total, so
        // the two must be the same question asked at two granularities and not two questions that happen to agree.
        var debtStatuses = PlanBillingRules.DebtBearingPlanStatuses;
        var excluded = excludedPlanIds as ICollection<Guid> ?? excludedPlanIds.ToList();

        var totals = await _context.TreatmentPlans
            .Where(p => p.ClinicId == clinicId
                        && debtStatuses.Contains(p.Status)
                        && !excluded.Contains(p.Id))
            .SelectMany(p => p.Installments)
            .SelectMany(i => i.Payments)
            .Where(pay => !pay.IsVoided && pay.PaidOn >= from && pay.PaidOn <= toInclusive)
            .GroupBy(pay => pay.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(pay => pay.Amount) })
            .ToListAsync(cancellationToken);

        return totals.Select(t => new PaymentMethodTotal(t.Method, t.Amount)).ToList();
    }

    public async Task<IReadOnlyList<CaisseInstallmentPaymentRow>> GetInstallmentChequePaymentsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> excludedPlanIds,
        DateTime? dueFrom = null,
        DateTime? dueTo = null,
        CancellationToken cancellationToken = default)
    {
        // The devis half of « chèques à encaisser ». Same projection as the statement's rows, different question:
        // which cheques still have to be presented, whenever they were taken.
        //
        // The bridged-plan exclusion is not cosmetic here. IssueInvoiceCommand carries a bridged plan's cheque
        // across onto the invoice payment, so without it one physical cheque would be listed twice — and the two
        // rows would look exactly like two genuine cheques of the same amount from the same bank. It is also what
        // makes a cheque un-markable twice (B-1): once the plan is bridged only the invoice-side row is reachable.
        //
        // ⚠️ Banked cheques are returned and the caller filters — see the invoice-side twin for why.
        var debtStatuses = PlanBillingRules.DebtBearingPlanStatuses;
        var excluded = excludedPlanIds as ICollection<Guid> ?? excludedPlanIds.ToList();

        var rows = await _context.TreatmentPlans
            .Where(p => p.ClinicId == clinicId
                        && debtStatuses.Contains(p.Status)
                        && !excluded.Contains(p.Id))
            .SelectMany(plan => plan.Installments
                .SelectMany(i => i.Payments
                    .Where(pay => !pay.IsVoided
                                  && pay.Method == PaymentMethod.Cheque
                                  && (pay.ChequeDueDate == null
                                      || ((dueFrom == null || pay.ChequeDueDate >= dueFrom)
                                          && (dueTo == null || pay.ChequeDueDate <= dueTo))))
                    .Select(pay => new
                    {
                        PaymentId = pay.Id,
                        TreatmentPlanId = plan.Id,
                        InstallmentId = i.Id,
                        PlanNumber = plan.Number,
                        plan.PatientId,
                        pay.Amount,
                        pay.Method,
                        pay.PaidOn,
                        pay.ChequeNumber,
                        pay.ChequeBankName,
                        pay.ChequeDueDate,
                        pay.ChequeBankedOn,
                        pay.ChequeBankedByName
                    })))
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CaisseInstallmentPaymentRow(
                r.PaymentId, r.TreatmentPlanId, r.InstallmentId, r.PlanNumber, r.PatientId,
                r.Amount, r.Method, r.PaidOn, IsVoided: false, VoidReason: null, VoidedByName: null,
                r.ChequeNumber, r.ChequeBankName, r.ChequeDueDate,
                r.ChequeBankedOn, r.ChequeBankedByName))
            .ToList();
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

    public async Task<IReadOnlyList<Guid>> GetDebtBearingItemIdsAsync(
        Guid clinicId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        if (itemIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        // Through PlanBillingRules, never a retyped status list: « which devis carry patient debt » is the same
        // question the four money reads ask, and a second copy here would drift from them the first time a status
        // moved. An id projection — the caller needs a set, not the plans.
        return await _context.TreatmentPlans
            .Where(p => p.ClinicId == clinicId
                        && PlanBillingRules.DebtBearingPlanStatuses.Contains(p.Status))
            .SelectMany(p => p.Items)
            .Where(i => itemIds.Contains(i.Id))
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
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
                .ThenInclude(i => i.Steps)
            .Include(p => p.Installments)
            .ThenInclude(i => i.Payments)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (plan != null)
        {
            _context.TreatmentPlans.Remove(plan);
        }
    }
}
