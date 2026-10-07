using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// The cloud's PC de secours watch (<c>clinic-pc-copy</c>), every minute and per cabinet:
/// <list type="bullet">
///   <item>the admins' bell is made to say what <see cref="RelayAlertRules"/> says is true now (AC-2.2, EC-9, EC-10);</item>
///   <item>the vendor is e-mailed <b>once</b> when a problem of <see cref="RelayVendorAlertRules"/> starts, and the
///   episode is closed when it ends (AC-9.2, AC-9.4).</item>
/// </list>
///
/// <para>A cabinet whose PC was retired (or whose relay row is gone) still gets one pass, with nothing wanted, so its
/// old rows are withdrawn and its episodes closed rather than left asserting a problem about a PC that is gone.</para>
/// </summary>
public class RelayWatchJob
{
    private readonly IClinicRelayRepository _relays;
    private readonly IClinicRepository _clinics;
    private readonly IStaffNotificationRepository _notifications;
    private readonly INotificationGenerator _generator;
    private readonly IRelayIncidentRepository _incidents;
    private readonly IPlatformAccountRepository _accounts;
    private readonly ITransactionalEmailSender _email;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<RelayWatchJob> _logger;

    public RelayWatchJob(
        IClinicRelayRepository relays,
        IClinicRepository clinics,
        IStaffNotificationRepository notifications,
        INotificationGenerator generator,
        IRelayIncidentRepository incidents,
        IPlatformAccountRepository accounts,
        ITransactionalEmailSender email,
        IUnitOfWork unitOfWork,
        IConfiguration configuration,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<RelayWatchJob> logger)
    {
        _relays = relays;
        _clinics = clinics;
        _notifications = notifications;
        _generator = generator;
        _incidents = incidents;
        _accounts = accounts;
        _email = email;
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 0)]
    public async Task WatchRelays()
    {
        _auditActor.RunAs(nameof(RelayWatchJob));
        _tenantScope.UseSystemWide("RelayWatchJob keeps every clinic's PC de secours bell rows and vendor alerts in step");

        var now = DateTime.UtcNow;
        var watched = new List<(ClinicRelay Relay, Clinic? Clinic)>();

        foreach (var relay in CurrentPerClinic(await _relays.GetLiveAsync(), now))
        {
            Clinic? clinic = null;
            try
            {
                clinic = await _clinics.GetByIdAsync(relay.ClinicId);
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

            watched.Add((relay, clinic));
        }

        var watchedClinics = watched.Select(w => w.Relay.ClinicId).ToHashSet();
        foreach (var clinicId in await _notifications.GetClinicIdsWithRelayAlertsAsync())
        {
            if (!watchedClinics.Contains(clinicId))
            {
                await _generator.SyncRelayAlertsAsync(clinicId, Array.Empty<RelayAlertRow>());
            }
        }

        try
        {
            await AlertVendorAsync(watched, now);
        }
        catch (Exception ex)
        {
            // The bell above is already in step; a vendor-alert failure is retried by the next minute's pass.
            _logger.LogError(ex, "PC de secours vendor alert pass failed");
        }
    }

    /// <summary>
    /// Opens an episode for each problem that started, closes each one that ended, commits, and only then e-mails the
    /// new ones — an e-mail sent for an episode that failed to save would be sent again a minute later.
    /// </summary>
    private async Task AlertVendorAsync(IReadOnlyList<(ClinicRelay Relay, Clinic? Clinic)> watched, DateTime now)
    {
        var open = await _incidents.GetOpenAsync();
        var started = new List<(RelayIncident Incident, string? ClinicName, DateTime? Since)>();
        var stillDue = new HashSet<RelayIncident>();

        foreach (var (relay, clinic) in watched)
        {
            var mine = open.Where(i => i.RelayId == relay.Id).ToList();
            var due = RelayVendorAlertRules.Due(relay, clinic?.WorkingHoursJson, mine.Select(i => i.Kind).ToList(), now);

            foreach (var kind in due)
            {
                var existing = mine.FirstOrDefault(i => i.Kind == kind);
                if (existing is not null)
                {
                    stillDue.Add(existing);
                    continue;
                }

                var incident = RelayIncident.Start(relay.ClinicId, relay.Id, kind, now);
                await _incidents.AddAsync(incident);
                started.Add((incident, clinic?.Name, ClinicRelayHealth.Read(relay, now).Since));
            }
        }

        var ended = open.Where(i => !stillDue.Contains(i)).ToList();
        foreach (var incident in ended)
        {
            incident.End(now);
        }

        if (started.Count == 0 && ended.Count == 0)
        {
            return;
        }

        await _unitOfWork.SaveChangesAsync();

        if (started.Count == 0)
        {
            return;
        }

        if (!_email.IsConfigured)
        {
            _logger.LogError("PC de secours alert NOT e-mailed: no SMTP is configured on this deployment. "
                             + "The console's « PC de secours » column is the only place this is shown.");
            return;
        }

        var recipients = await VendorAlertRecipients.ResolveAsync(
            _configuration[VendorAlertRecipients.AlertEmailKey], _accounts);
        if (recipients.Count == 0)
        {
            _logger.LogError("PC de secours alert NOT e-mailed: {Key} is empty and there is no active console account.",
                VendorAlertRecipients.AlertEmailKey);
            return;
        }

        foreach (var (incident, clinicName, since) in started)
        {
            var content = RelayVendorAlertEmail.Compose(clinicName, incident.ClinicId, incident.Kind, since, now);
            var subject = RelayVendorAlertEmail.Subject(clinicName, incident.Kind);
            var sentToAnyone = false;
            foreach (var recipient in recipients)
            {
                var outcome = await _email.SendAsync(recipient, subject, content);
                if (outcome.Outcome == TransactionalEmailOutcome.Sent)
                {
                    sentToAnyone = true;
                }
                else
                {
                    _logger.LogError("PC de secours alert to a console recipient failed: {Outcome} {Error}",
                        outcome.Outcome, outcome.Error);
                }
            }

            if (sentToAnyone)
            {
                incident.MarkEmailed(DateTime.UtcNow);
            }
        }

        await _unitOfWork.SaveChangesAsync();
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
