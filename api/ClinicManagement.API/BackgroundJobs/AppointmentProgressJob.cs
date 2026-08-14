using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// Minutely pass that moves a booked visit to <see cref="AppointmentStatus.InProgress"/> once its own slot has
/// begun — 14:00→15:00 reads « En cours » at 14:16 without anybody pressing anything.
///
/// <b>Why a job and not a write hook.</b> <see cref="StockExpiryJob"/>'s reasoning exactly: a slot is entered by
/// the <i>passage of time</i>, and no write happens at the minute it starts. A write-triggered implementation
/// would fire for every visit except the one case it exists for.
///
/// <b>What it deliberately does not do.</b> It only ever <i>starts</i> a visit. It never closes one and never
/// marks an absence — leaving a slot is not evidence the patient came, so « Terminé » and « Absent » stay
/// human decisions, and a visit whose window has passed keeps whatever status it holds.
///
/// Runs unconditionally and is <b>not</b> connectivity-gated: it writes a status, so it must work on an offline
/// LAN install. It is also not gated on the cabinet's entitlement — like the backup and expiry passes it records
/// nothing new, it advances the state of work already booked. Per-clinic failures are logged and skipped.
/// </summary>
public class AppointmentProgressJob
{
    /// <summary>
    /// How far back the SQL window reaches. A visit whose slot contains this minute began at most its own
    /// duration ago, and a séance is a clinic visit — one day covers anything one can plausibly be. See
    /// <see cref="IAppointmentRepository.GetRunningNotStartedAsync"/> for the residual this bound leaves and why
    /// its direction is safe.
    /// </summary>
    private static readonly TimeSpan LongestVisit = TimeSpan.FromDays(1);

    /// <summary>
    /// The key the appointment <i>commands</i> broadcast, asked of the production resolver rather than typed as
    /// <c>"appointments"</c> here. A literal would be a second authority over a contract
    /// <c>RealtimeResourceResolverTests</c> holds against the frontend, and it would drift silently — a wrong key
    /// is a broadcast nobody listens for, which looks exactly like the job not running.
    /// </summary>
    private static readonly string AppointmentsResource =
        RealtimeResourceResolver.Resolve(typeof(UpdateAppointmentCommand))!;

    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<AppointmentProgressJob> _logger;

    public AppointmentProgressJob(
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<AppointmentProgressJob> logger)
    {
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public Task StartRunningAppointments() => StartRunningAppointments(DateTime.UtcNow);

    /// <summary>
    /// Takes the instant as a parameter for <see cref="SubscriptionWarningJob"/>'s reason: the whole behaviour is
    /// a clock boundary, and one that reads its own clock is untestable at the minute that matters.
    /// </summary>
    public async Task StartRunningAppointments(DateTime nowUtc)
    {
        // A job has no token; without this every row it writes reads « Tâche automatique » with no clue which one.
        _auditActor.RunAs(nameof(AppointmentProgressJob));

        // US-2: Appointment is clinic-filtered, and this pass covers every clinic. Without the declaration it
        // would find nothing anywhere and log a clean run — R-1's failure mode exactly.
        _tenantScope.UseSystemWide("AppointmentProgressJob scans every clinic for visits whose slot has begun");

        var running = await _appointmentRepository.GetRunningNotStartedAsync(nowUtc, LongestVisit);

        foreach (var clinic in running.GroupBy(a => a.ClinicId))
        {
            try
            {
                await StartClinicAsync(clinic.Key, clinic.ToList());
            }
            catch (Exception ex)
            {
                // One clinic's failure must not stop the others — the pass is per-clinic independent.
                _logger.LogError(ex, "Auto-start pass failed for clinic {ClinicId}", clinic.Key);
            }
        }
    }

    private async Task StartClinicAsync(Guid clinicId, IReadOnlyList<Appointment> appointments)
    {
        var started = 0;

        foreach (var appointment in appointments)
        {
            // The read already excludes both of these. Asking again keeps that agreement checkable here rather
            // than turning a widened predicate into a thrown transition or an empty save — and the two terms are
            // separate because `CanTransition` counts re-assigning the current status as legal, so the guard
            // alone would let an already-started visit through to a `Start()` that changes nothing.
            if (appointment.Status == AppointmentStatus.InProgress
                || !Appointment.CanTransition(appointment.Status, AppointmentStatus.InProgress))
            {
                continue;
            }

            appointment.Start();
            await _appointmentRepository.UpdateAsync(appointment);
            started++;
        }

        if (started == 0)
        {
            return;
        }

        // One save per clinic, so a refused write costs that clinic's batch and no other's. No
        // SetExpectedVersion: nobody is editing a form here, and a user's concurrent edit legitimately wins.
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Auto-started {Count} appointment(s) for clinic {ClinicId}", started, clinicId);

        // Post-commit and additive: an open agenda repaints without a refresh, and a failed broadcast is
        // swallowed by the notifier rather than costing the statuses that are already saved.
        await _realtime.NotifyEntityChangedAsync(clinicId, AppointmentsResource);
    }
}
