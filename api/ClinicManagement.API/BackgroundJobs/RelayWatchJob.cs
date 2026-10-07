using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// The cloud's PC de secours watch (<c>clinic-pc-copy</c> AC-2.2, EC-9, EC-10): every minute, per cabinet, the admin
/// bell is made to say what <see cref="RelayAlertRules"/> says is true now — and nothing else.
///
/// <para>A cabinet whose PC was retired (or whose relay row is gone) still gets one pass, with nothing wanted, so its
/// old rows are withdrawn rather than left asserting a problem about a PC that is no longer there.</para>
/// </summary>
public class RelayWatchJob
{
    private readonly IClinicRelayRepository _relays;
    private readonly IClinicRepository _clinics;
    private readonly IStaffNotificationRepository _notifications;
    private readonly INotificationGenerator _generator;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<RelayWatchJob> _logger;

    public RelayWatchJob(
        IClinicRelayRepository relays,
        IClinicRepository clinics,
        IStaffNotificationRepository notifications,
        INotificationGenerator generator,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<RelayWatchJob> logger)
    {
        _relays = relays;
        _clinics = clinics;
        _notifications = notifications;
        _generator = generator;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0)]
    public async Task WatchRelays()
    {
        _auditActor.RunAs(nameof(RelayWatchJob));
        _tenantScope.UseSystemWide("RelayWatchJob keeps every clinic's PC de secours bell rows in step");

        var now = DateTime.UtcNow;
        var watched = new HashSet<Guid>();

        foreach (var relay in CurrentPerClinic(await _relays.GetLiveAsync(), now))
        {
            watched.Add(relay.ClinicId);
            try
            {
                var clinic = await _clinics.GetByIdAsync(relay.ClinicId);
                var shown = (await _notifications.GetRelayAlertsAsync(relay.ClinicId))
                    .Select(n => n.RelayAlert)
                    .OfType<RelayAlert>()
                    .ToList();
                var wanted = RelayAlertRules.Wanted(relay, clinic?.WorkingHoursJson, shown, now);
                await _generator.SyncRelayAlertsAsync(relay.ClinicId, wanted);
            }
            catch (Exception ex)
            {
                // One cabinet's failure must not cost every other cabinet its bell.
                _logger.LogError(ex, "PC de secours watch failed for clinic {ClinicId}", relay.ClinicId);
            }
        }

        foreach (var clinicId in await _notifications.GetClinicIdsWithRelayAlertsAsync())
        {
            if (!watched.Contains(clinicId))
            {
                await _generator.SyncRelayAlertsAsync(clinicId, Array.Empty<RelayAlertRow>());
            }
        }
    }

    /// <summary>One row per cabinet: the one holding its place, else the newest (a lapsed code beside nothing).</summary>
    public static IEnumerable<ClinicRelay> CurrentPerClinic(IEnumerable<ClinicRelay> live, DateTime nowUtc) =>
        live.GroupBy(r => r.ClinicId)
            .Select(g => g
                .OrderByDescending(r => r.OccupiesTheClinic(nowUtc))
                .ThenByDescending(r => r.CreatedAtUtc)
                .ThenByDescending(r => r.Id)
                .First());
}
