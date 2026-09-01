using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Pulls each connected clinic's Google events into its appointments on a schedule (Google→App).
///
/// <b>Why it exists.</b> That direction had <i>no</i> job at all — its only caller was the « Importer depuis
/// Google » button — so an appointment typed straight into Google never reached the app until somebody
/// remembered to press it. The other direction has always been automatic (the push runs inline, post-commit,
/// from the appointment handlers), which made the asymmetry easy to miss: the app's own bookings appeared in
/// Google immediately, so the sync looked alive.
///
/// <b>Why it could not simply be registered before.</b> The sync resolved its clinic from
/// <c>ICurrentClinicResolver</c>, i.e. from the HTTP context. A job has none, so the clinic was unresolvable —
/// and a job's <see cref="ITenantScope"/> is <c>Unset</c>, which reads <b>zero rows</b> and logs a clean pass.
/// The clinic is a parameter now, and this declares its scope like every other recurring job.
///
/// Not connectivity-gated at the job level: <see cref="IGoogleCalendarSyncService"/> already skips a clinic with
/// no connection, and Google being unreachable surfaces as that clinic's own logged failure rather than as a
/// reason to skip every other practice.
/// </summary>
public class GoogleCalendarImportJob
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IGoogleCalendarSyncService _syncService;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<GoogleCalendarImportJob> _logger;

    public GoogleCalendarImportJob(
        IClinicRepository clinicRepository,
        IGoogleCalendarSyncService syncService,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<GoogleCalendarImportJob> logger)
    {
        _clinicRepository = clinicRepository;
        _syncService = syncService;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    /// <summary>
    /// ⚠️ <c>DisableConcurrentExecution</c> is load-bearing here, not hygiene: two overlapping passes would both
    /// see the same unlinked Google event and both create an appointment for it, because the link is written after
    /// the read. The timeout is generous — the window is 97 days per clinic and Google is a network hop.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 900)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ImportFromGoogleCalendar()
    {
        // A job has no token, so without naming itself every row it writes reads « Tâche automatique » with no
        // clue which pass wrote it. Declared before anything is saved — see IAuditActorProvider.RunAs.
        _auditActor.RunAs(nameof(GoogleCalendarImportJob));

        // The clinic list is unfiltered, but everything the sync reads underneath (appointments, patients) is
        // clinic-filtered, so without this the pass would find nothing anywhere and log a clean run.
        _tenantScope.UseSystemWide("GoogleCalendarImportJob imports each connected clinic's Google events");

        var clinics = await _clinicRepository.GetAllAsync();

        // Only the practices that have actually connected Google. Reading the token here rather than letting the
        // sync skip keeps the log honest: « 2 clinics » should mean two imports were attempted.
        var connected = clinics
            .Where(c => !string.IsNullOrEmpty(c.GoogleRefreshTokenProtected) || !string.IsNullOrEmpty(c.GoogleRefreshToken))
            .ToList();

        if (connected.Count == 0)
        {
            _logger.LogDebug("No clinic has connected Google Calendar; nothing to import.");
            return;
        }

        _logger.LogInformation("Importing Google Calendar events for {Count} connected clinic(s)", connected.Count);

        foreach (var clinic in connected)
        {
            try
            {
                // The pass names itself, so its `CalendarImportRun` reads « Import automatique » rather than
                // « Import manuel » — and, more to the point, so a practice can tell the import it never asked
                // for from the one it pressed itself. Both are undoable: a pass nobody clicked is precisely the
                // one that needs to be.
                await _syncService.SyncGoogleCalendarToAppointmentsAsync(
                    clinic.Id, CalendarImportRun.JobActorPrefix + nameof(GoogleCalendarImportJob));
            }
            catch (Exception ex)
            {
                // One clinic's failure — an expired token, an unreadable one, Google refusing — must not stop the
                // others. The sync swallows its own inner errors; this catches the ones it rethrows.
                _logger.LogError(ex, "Google Calendar import failed for clinic {ClinicId}", clinic.Id);
            }
        }
    }
}
