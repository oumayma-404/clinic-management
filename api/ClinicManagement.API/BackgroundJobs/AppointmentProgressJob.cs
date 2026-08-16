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
/// <b>What it deliberately does not do.</b> It never marks a visit « Terminé » and never marks an absence:
/// leaving a slot is not evidence the patient came, so both stay human decisions. Its second pass moves an
/// elapsed visit to <see cref="AppointmentStatus.AwaitingClosure"/> — « Séance passée » — which asserts only
/// that the slot has ended, and closes a « créneau occupé » outright, that having nobody to ask about.
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
    /// How far back the elapse pass reaches — wider than <see cref="LongestVisit"/> because it corrects a
    /// *backlog*, not a moment: a clinic PC switched off for a holiday comes back with a fortnight of visits
    /// still reading « En cours ». Served by <c>IX_Appointments_Status_AppointmentDateTime</c>.
    /// </summary>
    private static readonly TimeSpan ElapsedLookback = TimeSpan.FromDays(30);

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

    /// <summary>
    /// The tick: start what has begun, then close what has ended. Both passes, one entry point, so a slot that
    /// begins and ends between two ticks still reaches its terminal state — and forwards, in the order the
    /// lifecycle actually runs, so the audit ledger reads as a story rather than backwards.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public async Task StartRunningAppointments()
    {
        var nowUtc = DateTime.UtcNow;
        await StartRunningAppointments(nowUtc);
        await CloseElapsedAppointments(nowUtc);
    }

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

    /// <summary>
    /// Moves every visit whose slot has <b>ended</b> out of the statuses that assert it is still happening.
    ///
    /// <para>Two outcomes, decided by whether anybody is expected: a patient-bearing visit becomes
    /// <see cref="AppointmentStatus.AwaitingClosure"/> — « Séance passée », the presence still unanswered — while a
    /// « créneau occupé » is simply <see cref="AppointmentStatus.Completed"/>, because a blocked hour has nothing
    /// to close and nobody to ask about.</para>
    ///
    /// <para>This does <b>not</b> weaken the pass's standing rule that « Terminé » and « Absent » are human
    /// decisions: neither is what a patient-bearing visit gets here. What it fixes is that the alternative used to
    /// be « En cours », which asserts somebody is in the chair.</para>
    /// </summary>
    public async Task CloseElapsedAppointments(DateTime nowUtc)
    {
        _auditActor.RunAs(nameof(AppointmentProgressJob));
        _tenantScope.UseSystemWide("AppointmentProgressJob scans every clinic for visits whose slot has ended");

        var elapsed = await _appointmentRepository.GetElapsedOpenAsync(nowUtc, ElapsedLookback);

        foreach (var clinic in elapsed.GroupBy(a => a.ClinicId))
        {
            try
            {
                await CloseClinicAsync(clinic.Key, clinic.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Elapse pass failed for clinic {ClinicId}", clinic.Key);
            }
        }
    }

    private async Task CloseClinicAsync(Guid clinicId, IReadOnlyList<Appointment> appointments)
    {
        var closed = 0;

        foreach (var appointment in appointments)
        {
            // A blocked slot is time the practitioner reserved, so its passing closes it outright; a visit keeps
            // its presence question open for a human.
            var target = appointment.PatientId.HasValue
                ? AppointmentStatus.AwaitingClosure
                : AppointmentStatus.Completed;

            // Both terms, for the start pass's reason: `CanTransition` counts a self-assignment as legal, so the
            // guard alone would let an already-closed visit through to an empty save and an audit row a minute.
            if (appointment.Status == target || !Appointment.CanTransition(appointment.Status, target))
            {
                continue;
            }

            if (target == AppointmentStatus.AwaitingClosure)
            {
                appointment.MarkAwaitingClosure();
            }
            else
            {
                appointment.Complete();
            }

            await _appointmentRepository.UpdateAsync(appointment);
            closed++;
        }

        if (closed == 0)
        {
            return;
        }

        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Closed {Count} elapsed appointment(s) for clinic {ClinicId}", closed, clinicId);

        await _realtime.NotifyEntityChangedAsync(clinicId, AppointmentsResource);
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
