using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Daily pass that warns each cabinet before its entitlement to record new work ends
/// (<c>clinic-subscription</c> FR-5, AC-3.4–3.7): one in-app notification per threshold crossed — 7, 3 and 1 day(s)
/// before the end date, and again on the day itself.
///
/// <para><b>Why a job and not a write hook</b>, the same argument as <see cref="StockExpiryJob"/>: a date arriving
/// is crossed by the <i>passage of time</i>, and nothing happens on the day a cabinet enters the window. A
/// write-triggered implementation would be a warning that never fires for the cabinet quietly working through its
/// last week, which is the only case it exists for.</para>
///
/// <para><b>Not connectivity-gated</b>, like <see cref="StockExpiryJob"/> and <see cref="BackupJob"/>: the warning is
/// in-app. It is also never an OS push (AC-3.6) — <c>StaffNotificationRules.ReachesALockedPhone</c> answers
/// <c>false</c> for the category, so the push decorator passes it straight through.</para>
///
/// <para>⚠️ <b>Two states are deliberately left exactly as they are.</b> A <b>suspended</b> cabinet is not warned:
/// <see cref="SubscriptionStateReader"/> surfaces no countdown for one on purpose (EC-11), and « votre abonnement se
/// termine dans 3 jours » is the wrong thing to tell a practice suspended for another reason — paying would not fix
/// it. An <b>expired</b> one is not warned either, and just as importantly its existing rows are <b>not cleared</b>:
/// the cabinet is now meeting a refused save, and those four rows are what explain it. Only an extension past the
/// window withdraws them, which is what re-arms the thresholds.</para>
/// </summary>
public class 
    
    SubscriptionWarningJob
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IClinicSubscriptionRepository _subscriptions;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly ISubscriptionPolicy _policy;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<SubscriptionWarningJob> _logger;

    public SubscriptionWarningJob(
        IClinicRepository clinicRepository,
        IClinicSubscriptionRepository subscriptions,
        INotificationGenerator notificationGenerator,
        ISubscriptionPolicy policy,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<SubscriptionWarningJob> logger)
    {
        _clinicRepository = clinicRepository;
        _subscriptions = subscriptions;
        _notificationGenerator = notificationGenerator;
        _policy = policy;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [AutomaticRetry(Attempts = 3)]
    public Task WarnExpiringSubscriptions() => WarnExpiringSubscriptions(ClinicClock.ClinicToday());

    /// <summary>
    /// The pass over an explicit clinic-local day — the same reason <see cref="SubscriptionStateReader"/> takes one
    /// rather than reading the clock: the four thresholds and the midnight they turn on are otherwise untestable,
    /// and midnight is the only boundary that matters for a date that arrives by itself. The overload above is the
    /// sole production caller.
    /// </summary>
    public async Task WarnExpiringSubscriptions(DateTime clinicToday)
    {
        // Registered only where subscriptions are enforced, and asked again here: a reprofiled install can still
        // hold the recurring registration in Hangfire storage and fire it before Program.cs drops it.
        if (!_policy.RequiresSubscription)
        {
            return;
        }

        // I6: a job has no token, so without naming itself every row it writes would read « Tâche automatique »
        // with no clue which one.
        _auditActor.RunAs(nameof(SubscriptionWarningJob));

        // US-2: both entitlement tables and StaffNotification are clinic-filtered, so the pass needs every
        // cabinet. Unscoped it would find no entitlements anywhere and log a clean run — R-1's failure mode.
        _tenantScope.UseSystemWide("SubscriptionWarningJob reviews every cabinet's entitlement end date");

        // One « today » for the whole pass — resolved by the caller above, so two cabinets reviewed either side of
        // Tunisian midnight cannot be measured against different days.
        var clinics = await _clinicRepository.GetAllAsync();

        foreach (var clinic in clinics)
        {
            try
            {
                await ReviewClinicAsync(clinic.Id, clinicToday);
            }
            catch (Exception ex)
            {
                // One cabinet's failure must not stop the others — the review is per-cabinet independent.
                _logger.LogError(ex, "Subscription warning review failed for clinic {ClinicId}", clinic.Id);
            }
        }
    }

    private async Task ReviewClinicAsync(Guid clinicId, DateTime clinicToday)
    {
        var subscription = await _subscriptions.GetByClinicAsync(clinicId);
        if (subscription is null)
        {
            // Part A gives every cabinet an entitlement at both construction doors and verify-schema asserts it,
            // so this is a fault to make visible — not a reason to warn about a date we do not have, nor to clear
            // rows that may be the only record of a warning already given.
            _logger.LogWarning("Clinic {ClinicId} has no subscription entitlement; skipping warning review", clinicId);
            return;
        }

        var status = SubscriptionStateReader.Read(subscription, clinicToday);

        // See the ⚠️ on the class: neither state is warned about and neither has its rows withdrawn.
        if (status.State is SubscriptionState.Suspended or SubscriptionState.Expired)
        {
            return;
        }

        var threshold = SubscriptionStateReader.ThresholdReached(status.DaysRemaining);
        if (threshold is null || status.EndsOn is null)
        {
            // No end date, or one still beyond the window — including a grant that has just moved it there, which
            // is FR-5's re-arm: withdrawing the rows is what lets all four fire again next time.
            await _notificationGenerator.ClearSubscriptionWarningsAsync(clinicId);
            return;
        }

        await _notificationGenerator.EnsureSubscriptionWarningAsync(
            clinicId, threshold.Value, status.EndsOn.Value);
    }
}
