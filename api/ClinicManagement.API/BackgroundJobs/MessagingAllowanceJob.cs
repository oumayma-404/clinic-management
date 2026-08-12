using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// The WhatsApp reminder forfait's daily pass (<c>vendor-whatsapp-messaging-quota</c> D-2). Two duties in one loop —
/// <b>provision</b> each cabinet's counting row for the current Tunisian month (FR-1a), then <b>reconcile</b> the three
/// warnings (AC-3.6's withdrawal after a grant, AC-3.7's rollover withdraw-and-re-arm).
///
/// <para><b>⚠️ Provisioning runs FIRST, per cabinet</b> (R-9). It is the cheapest and the most load-bearing of the two:
/// a missing row is what makes « non mesuré » appear on a practice's own screen, and putting it behind anything that
/// could throw would cost a cabinet its counting row for the day.</para>
///
/// <para><b>⚠️ It is not the primary writer of either thing, and that is the design.</b> The counting row is
/// ensure-created by the dispatcher before the month's first send (§ 14a) precisely so a rollover does not park a
/// practice's reminders for up to 24 h waiting for this pass; and the thresholds are announced where the counter is
/// incremented (step 19), because consumption can cross all three between two sends. This is the <b>reconciling second
/// writer</b> — it catches a lost post-commit hook, corrects a snapshot that drifted from the fold (R-6), and performs
/// the two withdrawals nothing else can (a grant and a month turning are not sends).</para>
///
/// <para><b>Not connectivity-gated</b>, like <see cref="StockExpiryJob"/> and <see cref="SubscriptionWarningJob"/>: the
/// warnings are in-app and the provisioning is a database row. Neither needs egress.</para>
///
/// <para>⚠️ <b>A suspended or expired cabinet is not warned</b> (FR-6) — it is already refused for a reason this
/// warning does not explain, and « 95 % de votre forfait » would send a practice to buy messages it cannot send
/// anyway. Its existing rows are left alone rather than withdrawn: they were true when written, and
/// <see cref="SubscriptionWarningJob"/> makes the same call for the same reason. Provisioning still runs for it.</para>
/// </summary>
public class MessagingAllowanceJob
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IMessagingAllowanceRepository _allowances;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IVendorMessagingAvailability _availability;
    private readonly ISubscriptionPolicy _subscriptionPolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<MessagingAllowanceJob> _logger;

    public MessagingAllowanceJob(
        IClinicRepository clinicRepository,
        IMessagingAllowanceRepository allowances,
        IClinicSubscriptionRepository subscriptions,
        INotificationGenerator notificationGenerator,
        IVendorMessagingAvailability availability,
        ISubscriptionPolicy subscriptionPolicy,
        IUnitOfWork unitOfWork,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<MessagingAllowanceJob> logger)
    {
        _clinicRepository = clinicRepository;
        _allowances = allowances;
        _subscriptions = subscriptions;
        _notificationGenerator = notificationGenerator;
        _availability = availability;
        _subscriptionPolicy = subscriptionPolicy;
        _unitOfWork = unitOfWork;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    public Task ReviewMessagingAllowances() => ReviewMessagingAllowances(ClinicClock.ClinicToday());

    /// <summary>
    /// The pass over an explicit clinic-local day — <see cref="SubscriptionWarningJob"/>'s shape, and for its reason:
    /// the month boundary and everything that turns on it (a rollover withdrawing last month's rows, a send at 23:59
    /// on the 31st counting against that month) are otherwise untestable. The overload above is the sole production
    /// caller.
    /// </summary>
    public async Task ReviewMessagingAllowances(DateTime clinicToday)
    {
        // Registered only where the deployment sells vendor messaging, and asked again here: a reprofiled install can
        // still hold the recurring registration in Hangfire storage and fire it before Program.cs drops it (EC-16).
        if (!_availability.SellsVendorMessaging)
        {
            return;
        }

        // I6: a job has no token, so without naming itself every row it writes would read « Tâche automatique » with
        // no clue which one.
        _auditActor.RunAs(nameof(MessagingAllowanceJob));

        // US-2: both allowance tables and StaffNotification are clinic-filtered, so an unscoped pass would find no
        // rows anywhere and log a clean run — R-1's failure mode, and here it would silently stop provisioning.
        _tenantScope.UseSystemWide("MessagingAllowanceJob provisions and reconciles every cabinet's WhatsApp forfait");

        // One month for the whole pass — resolved by the caller above, so two cabinets reviewed either side of
        // Tunisian midnight cannot be measured against different months (EC-7).
        var monthKey = ClinicClock.MonthKey(clinicToday);
        var renewsOn = ClinicClock.FirstDayOfNextMonth(clinicToday);

        var clinics = await _clinicRepository.GetAllAsync();

        foreach (var clinic in clinics)
        {
            // R-9 — one try/catch per cabinet PER DUTY, and provisioning first. A throw in the warning reconciliation
            // must not cost this cabinet the counting row that was already written, and neither cabinet's failure may
            // stop the others.
            var month = await SafelyAsync(
                clinic.Id, "provision the counting row", () => ProvisionMonthAsync(clinic.Id, monthKey));

            await SafelyAsync(
                clinic.Id, "reconcile the forfait warnings",
                () => ReconcileWarningsAsync(clinic.Id, clinicToday, monthKey, renewsOn, month));
        }
    }

    /// <summary>
    /// FR-1a — the cabinet has a counting row for this month, and its allowance equals the fold.
    ///
    /// <para>⚠️ A cabinet whose ledger reaches this month with <b>nothing at all</b> gets no row (AC-4.3). A zeroed row
    /// would turn our own bookkeeping gap into the statement « the vendor allowed this practice nothing », and it would
    /// make « non mesuré » unreachable on the history screen for ever — the one reading that tells a broken counter
    /// apart from a quiet practice.</para>
    ///
    /// <para>⚠️ The existing row's allowance is <b>rewritten from the fold</b> when the two disagree (R-6). The refold
    /// is the primary writer; this is the reconciling backstop <c>verify-schema</c>'s
    /// <c>monthly-allowance-matches-ledger</c> would otherwise only be able to report.</para>
    /// </summary>
    private async Task<ClinicMessagingMonth?> ProvisionMonthAsync(Guid clinicId, string monthKey)
    {
        var entries = await _allowances.GetEntriesAsync(clinicId);
        var folded = MessagingAllowanceLedger.Fold(entries.Select(e => e.ToLedgerEntry()), monthKey);

        var existing = await _allowances.GetMonthAsync(clinicId, monthKey);
        if (existing is not null)
        {
            if (folded is { } current && current != existing.AllowanceMessages)
            {
                _logger.LogInformation(
                    "Rewriting clinic {ClinicId}'s {MonthKey} allowance snapshot from {Stored} to the folded {Folded}.",
                    clinicId, monthKey, existing.AllowanceMessages, current);

                existing.SetAllowance(current, DateTime.UtcNow);
                await _allowances.UpdateMonthAsync(existing);
                await _unitOfWork.SaveChangesAsync();
            }

            return existing;
        }

        if (folded is not { } allowance)
        {
            _logger.LogWarning(
                "Clinic {ClinicId} has no WhatsApp forfait reaching {MonthKey}; no counting row was created.",
                clinicId, monthKey);
            return null;
        }

        var row = ClinicMessagingMonth.For(clinicId, monthKey, allowance, DateTime.UtcNow);
        await _allowances.AddMonthAsync(row);
        await _unitOfWork.SaveChangesAsync();
        return row;
    }

    /// <summary>
    /// AC-3.6 and AC-3.7 — the cabinet carries exactly the warnings it currently meets, for exactly this month.
    ///
    /// <para>The withdrawal runs <b>before</b> the ensures and covers every month at once: last month's rows go
    /// (re-arming all three thresholds) and so do this month's thresholds a grant has put the cabinet back below,
    /// while the rows it still meets keep their read markers.</para>
    /// </summary>
    private async Task ReconcileWarningsAsync(
        Guid clinicId, DateTime clinicToday, string monthKey, DateTime renewsOn, ClinicMessagingMonth? month)
    {
        if (!await MayBeWarnedAsync(clinicId, clinicToday))
        {
            return;
        }

        var crossed = month is null
            // No counting row: nothing is measured, so no threshold is met. The rows of a *past* month still have to
            // be withdrawn, which is why this is a reconciliation with an empty set rather than an early return.
            ? Array.Empty<int>()
            : MessagingAllowanceThresholds.Crossed(month.ConsumedMessages, month.AllowanceMessages);

        await _notificationGenerator.ClearMessagingAllowanceWarningsAsync(clinicId, monthKey, crossed);

        if (month is null)
        {
            return;
        }

        foreach (var threshold in crossed)
        {
            await _notificationGenerator.EnsureMessagingAllowanceWarningAsync(
                clinicId, monthKey, threshold, month.AllowanceMessages, renewsOn);
        }
    }

    /// <summary>
    /// FR-6 — a suspended or expired cabinet is not warned. See the ⚠️ on the class for why its existing rows are left
    /// standing rather than withdrawn.
    ///
    /// <para>A cabinet with <b>no</b> entitlement row is warned normally: that is our own bookkeeping fault
    /// (<c>verify-schema</c>'s <c>every-clinic-has-an-entitlement</c> reports it), and silencing a practice's quota
    /// warnings over it would be invisible to the practice and unfixable by it — <c>OutboxSubscriptionGate</c>'s own
    /// reasoning.</para>
    /// </summary>
    private async Task<bool> MayBeWarnedAsync(Guid clinicId, DateTime clinicToday)
    {
        if (!_subscriptionPolicy.RequiresSubscription)
        {
            return true;
        }

        var subscription = await _subscriptions.GetByClinicAsync(clinicId);
        if (subscription is null)
        {
            return true;
        }

        return SubscriptionStateReader.Read(subscription, clinicToday).AllowsWrites;
    }

    private async Task<T?> SafelyAsync<T>(Guid clinicId, string duty, Func<Task<T?>> work)
    {
        try
        {
            return await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Duty} for clinic {ClinicId}", duty, clinicId);
            return default;
        }
    }

    private async Task SafelyAsync(Guid clinicId, string duty, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Duty} for clinic {ClinicId}", duty, clinicId);
        }
    }
}
