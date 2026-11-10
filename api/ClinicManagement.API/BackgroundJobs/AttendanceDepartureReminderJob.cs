using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Hangfire;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// « Départ non pointé » (<c>pointage-obligatoire</c> AC-6): 15 min after today's closing, every secretary still
/// « présente » gets one reminder, pushed to her phone. A job because closing time is crossed by the clock, not a write.
///
/// <para>⚠️ <b>It writes no pointage row</b> — every write ages a line's <c>xmin</c> and would 409 an admin's open
/// correction (the <c>AppointmentProgressJob</c> trap). « Once per line » is read off the bell instead: a reminder already
/// written for her today. Hours not set or a closed day: nothing, since « after closing » cannot be answered — nor a
/// cabinet with no live borne, where nobody could pointer the departure it asks for.</para>
/// </summary>
public class AttendanceDepartureReminderJob
{
    private readonly IClinicRepository _clinics;
    private readonly IAttendanceEntryRepository _entries;
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly IStaffNotificationRepository _notifications;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<AttendanceDepartureReminderJob> _logger;

    public AttendanceDepartureReminderJob(
        IClinicRepository clinics, IAttendanceEntryRepository entries, IAttendanceTerminalRepository terminals,
        IStaffNotificationRepository notifications, INotificationGenerator notificationGenerator,
        IAuditActorProvider auditActor, ITenantScope tenantScope, ILogger<AttendanceDepartureReminderJob> logger)
    {
        _clinics = clinics;
        _entries = entries;
        _terminals = terminals;
        _notifications = notifications;
        _notificationGenerator = notificationGenerator;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 240)]
    [AutomaticRetry(Attempts = 0)]
    public Task RemindMissingDepartures() => RemindMissingDepartures(DateTime.UtcNow);

    /// <summary>« Now » is a parameter so the 15-minute threshold and the cabinet's midnight are testable.</summary>
    public async Task RemindMissingDepartures(DateTime nowUtc)
    {
        _auditActor.RunAs(nameof(AttendanceDepartureReminderJob));
        _tenantScope.UseSystemWide("AttendanceDepartureReminderJob reads every cabinet's open pointage lines");

        var today = AttendanceRules.Today(nowUtc);
        var (dayStart, _) = ClinicClock.LocalDayRangeUtc(today.ToDateTime(TimeOnly.MinValue));
        foreach (var clinic in await _clinics.ListAttendanceHoursAsync())
        {
            try
            {
                if (AttendanceLockRules.ReminderDueUtc(today, clinic.WorkingHoursJson) is not { } due || nowUtc < due)
                    continue;

                var open = await _entries.ListOpenOnAsync(clinic.ClinicId, today);
                if (open.Count == 0)
                    continue;
                var bornes = (await _terminals.ListAsync(clinic.ClinicId)).Where(t => t.IsLive).Select(t => t.Name).ToList();
                if (bornes.Count == 0)
                    continue;

                foreach (var line in open)
                {
                    if (await _notifications.ExistsForUserSinceAsync(
                            clinic.ClinicId, NotificationCategory.AttendanceDepartureMissing, line.UserId, dayStart))
                        continue;
                    await _notificationGenerator.AttendanceDepartureMissingAsync(
                        clinic.ClinicId, line.UserId, AttendanceRules.LocalTime(line.ArrivalUtc), bornes);
                }
            }
            catch (Exception ex)
            {
                // One cabinet's failure must not cost the others their reminder.
                _logger.LogError(ex, "Departure reminder pass failed for clinic {ClinicId}", clinic.ClinicId);
            }
        }
    }
}
