using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Expenses;
using ClinicManagement.Application.Features.Expenses.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Daily pass that posts each active <see cref="RecurringExpense"/>'s missing months as ordinary
/// <see cref="Expense"/> rows — the loyer appears in la caisse without anybody typing it again.
///
/// <b>Why a job.</b> <see cref="StockExpiryJob"/>'s reasoning: a month is entered by the passage of time, and no
/// write happens on the day the rent falls due. A write-triggered implementation would fire for every case except
/// the one the feature exists for.
///
/// <b>Why it catches up rather than posting « today ».</b> A clinic PC switched off for a quarter comes back
/// owing three loyers. <see cref="MonthlyExpenseSchedule.DueMonths"/> returns every month between the series'
/// marker and the current one, so the gap is filled instead of silently swallowed — and the marker advancing per
/// month written is what makes a second run of the same day a no-op.
///
/// Runs unconditionally and is <b>not</b> connectivity-gated: it writes a database row, so it must work on an
/// offline LAN install. Per-clinic failures are logged and skipped.
/// </summary>
public class MonthlyExpenseJob
{
    /// <summary>
    /// The key the expense <i>commands</i> broadcast, asked of the production resolver rather than typed here — a
    /// literal would be a second authority over a contract <c>RealtimeResourceResolverTests</c> holds against the
    /// frontend, and a wrong key looks exactly like the job not running.
    /// </summary>
    private static readonly string ExpensesResource =
        RealtimeResourceResolver.Resolve(typeof(CreateExpenseCommand))!;

    private readonly IRecurringExpenseRepository _recurringExpenseRepository;
    private readonly IExpenseRepository _expenseRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<MonthlyExpenseJob> _logger;

    public MonthlyExpenseJob(
        IRecurringExpenseRepository recurringExpenseRepository,
        IExpenseRepository expenseRepository,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<MonthlyExpenseJob> logger)
    {
        _recurringExpenseRepository = recurringExpenseRepository;
        _expenseRepository = expenseRepository;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    public Task PostDueMonthlyExpenses() => PostDueMonthlyExpenses(DateTime.UtcNow);

    /// <summary>
    /// Takes the instant as a parameter for <see cref="AppointmentProgressJob"/>'s reason: the whole behaviour is
    /// a calendar boundary, and one that reads its own clock is untestable at the month end that matters.
    /// </summary>
    public async Task PostDueMonthlyExpenses(DateTime nowUtc)
    {
        // A job has no token; without this every row it writes reads « Tâche automatique » with no clue which one.
        _auditActor.RunAs(nameof(MonthlyExpenseJob));

        // RecurringExpense and Expense are both clinic-filtered and this pass covers every clinic. Without the
        // declaration it would find no series anywhere and log a clean run.
        _tenantScope.UseSystemWide("MonthlyExpenseJob posts every clinic's due monthly dépenses");

        var currentMonth = ClinicClock.CurrentMonthKey(nowUtc);
        var series = await _recurringExpenseRepository.GetActiveForPostingAsync();

        foreach (var clinic in series.GroupBy(s => s.ClinicId))
        {
            try
            {
                await PostClinicAsync(clinic.Key, clinic.ToList(), currentMonth);
            }
            catch (Exception ex)
            {
                // One clinic's failure must not stop the others — the pass is per-clinic independent.
                _logger.LogError(ex, "Monthly dépense pass failed for clinic {ClinicId}", clinic.Key);
            }
        }
    }

    private async Task PostClinicAsync(
        Guid clinicId,
        IReadOnlyList<RecurringExpense> series,
        string currentMonth)
    {
        var posted = 0;

        foreach (var recurring in series)
        {
            // The read already excludes a stopped series. Asking again keeps that agreement checkable here rather
            // than letting a widened predicate post months for a commitment the practice has ended.
            if (!recurring.IsActive)
            {
                continue;
            }

            foreach (var month in MonthlyExpenseSchedule.DueMonths(recurring.LastPostedMonth, currentMonth))
            {
                var expense = new Expense(
                    Guid.NewGuid(),
                    clinicId,
                    MonthlyExpenseSchedule.PostingDateUtc(month, recurring.DayOfMonth),
                    recurring.Category,
                    recurring.Amount,
                    recurring.Method,
                    recurring.Description,
                    recurring.Id);

                await _expenseRepository.AddAsync(expense);
                recurring.MarkPosted(month);
                posted++;
            }

            await _recurringExpenseRepository.UpdateAsync(recurring);
        }

        if (posted == 0)
        {
            return;
        }

        // ⚠️ ONE save per clinic, so the rows and the markers that record them commit together. Split, a failure
        // between the two would either post a month twice on the next run or lose it for ever — and this is money.
        // No SetExpectedVersion: nobody is editing a form here, and a user's concurrent edit legitimately wins.
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Posted {Count} monthly dépense(s) for clinic {ClinicId}", posted, clinicId);

        // Post-commit and additive: an open caisse repaints without a refresh, and a failed broadcast is
        // swallowed by the notifier rather than costing the dépenses that are already saved.
        await _realtime.NotifyEntityChangedAsync(clinicId, ExpensesResource);
    }
}
