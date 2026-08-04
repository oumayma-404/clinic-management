using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// The unattended backup (L4a/L4c/L4d) — the job that turns « quelqu'un se souvient de cliquer » into a
/// guarantee. Per clinic: run today's backup if it is due, prune old folders to the clinic's retention count,
/// and keep the staleness alert in step with reality.
///
/// <para><b>Not connectivity-gated</b>, unlike <see cref="NotificationJob"/> and
/// <see cref="EInvoiceOutboxJob"/> and for the same reason as <see cref="StockExpiryJob"/>: the output is a file
/// on a local disk, so it must work on an offline LAN install — which is the only kind of install this feature
/// exists for.</para>
///
/// <para>⚠️ <b>Registered hourly, not daily, and that is what makes the per-clinic hour real.</b> The schedule
/// belongs to the clinic (<see cref="Clinic.BackupHourLocal"/>, clinic-local), so a single daily Hangfire cron
/// could only ever honour one of them. Running hourly and asking each clinic « is it your hour yet, and have you
/// already been done today? » also buys the case a fixed 02:00 cron cannot serve at all: a clinic PC that is
/// switched off overnight. A daily job at 02:00 on such a machine never runs — silently, for ever. This one backs
/// up the first hour after the machine is on past its window.</para>
///
/// <para>⚠️ <c>DisableConcurrentExecution</c> is load-bearing here rather than defensive: two overlapping runs
/// would have two <c>pg_dump</c> processes writing two folders while both prune, and the pruner's
/// « never delete the last surviving backup » floor is evaluated per run.</para>
/// </summary>
public class BackupJob
{
    /// <summary>
    /// After a failed attempt, how long before this job tries that clinic again. Bounded on purpose: without it
    /// an hourly job on a clinic whose USB disk is unplugged would write ~20 failure rows a day, and a history
    /// list nobody can read is the same as no history at all. Four attempts a day is enough to catch « the disk
    /// was plugged back in at lunchtime ».
    /// </summary>
    private const int RetryQuietHours = 6;

    private readonly IClinicRepository _clinicRepository;
    private readonly IBackupRunRepository _backupRuns;
    private readonly IBackupService _backupService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IAuditActorProvider _auditActor;
    private readonly ILogger<BackupJob> _logger;

    public BackupJob(
        IClinicRepository clinicRepository,
        IBackupRunRepository backupRuns,
        IBackupService backupService,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator,
        IAuditActorProvider auditActor,
        ILogger<BackupJob> logger)
    {
        _clinicRepository = clinicRepository;
        _backupRuns = backupRuns;
        _backupService = backupService;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
        _auditActor = auditActor;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunScheduledBackups()
    {
        // I6: a job has no token, so without naming itself every row it writes would read « Tâche automatique »
        // with no clue which one.
        _auditActor.RunAs(nameof(BackupJob));

        var clinics = await _clinicRepository.GetAllAsync();

        foreach (var clinic in clinics)
        {
            try
            {
                await ProcessClinicAsync(clinic);
            }
            catch (Exception ex)
            {
                // One clinic's failure must not stop the others. A failure that reaches here is a bug rather
                // than a backup problem — the backup's own failures are recorded on the run row below.
                _logger.LogError(ex, "Scheduled backup failed for clinic {ClinicId}", clinic.Id);
            }
        }
    }

    private async Task ProcessClinicAsync(Clinic clinic)
    {
        if (clinic.BackupEnabled && await IsDueAsync(clinic))
        {
            await RunBackupAsync(clinic);
        }

        // The staleness check runs whether or not a backup was attempted, and whether or not the schedule is
        // enabled — a clinic that has switched the schedule off still needs to be told that nothing has been
        // backed up since Tuesday. That is the whole point of the alert: it is about the *state of the data*,
        // not about whether a job ran.
        await EvaluateStalenessAsync(clinic);
    }

    /// <summary>
    /// Is this clinic due? Three questions, in the order that costs least: has it already been done today, has an
    /// attempt just failed, and has its hour arrived?
    /// </summary>
    private async Task<bool> IsDueAsync(Clinic clinic)
    {
        var todayLocal = ClinicClock.ClinicToday();
        var startOfDay = ClinicClock.StartOfLocalDayUtc(todayLocal);

        var lastRun = await _backupRuns.GetLastRunAsync(clinic.Id);

        // Already succeeded today → done. « Today » is the clinic's own day, not UTC's: a 02:00 Tunis backup is
        // 01:00 UTC, so a UTC day boundary would file it under the previous day and the check would fire twice.
        if (lastRun != null
            && lastRun.Outcome == BackupOutcome.Succeeded
            && lastRun.StartedAt >= startOfDay)
        {
            return false;
        }

        // An attempt within the quiet window → wait. Covers both a recent failure and a Running row left behind
        // by a crash, which must not be joined by a second dump while the first may still hold a lock.
        if (lastRun != null && lastRun.StartedAt > DateTime.UtcNow.AddHours(-RetryQuietHours))
        {
            return false;
        }

        // At or past the clinic's own hour. `>=` and not `==` so a machine that was off at 02:00 is caught the
        // first hour it is on — the case a fixed daily cron silently never serves.
        return ClinicClock.ToClinicLocal(DateTime.UtcNow).Hour >= clinic.BackupHourLocal;
    }

    /// <summary>
    /// One attempt, recorded whatever happens (L4d).
    ///
    /// <para>⚠️ The <c>Running</c> row is written and <b>committed before</b> the dump starts. That ordering is
    /// the point: a crash or a power cut mid-dump then leaves a visible <c>Running</c> row instead of no row at
    /// all, and « rien ce soir-là » is exactly the reading that loses a practice its data. It also serialises the
    /// retry-quiet-window check above against a job that dies without unwinding.</para>
    /// </summary>
    private async Task RunBackupAsync(Clinic clinic)
    {
        var run = new BackupRun(Guid.NewGuid(), clinic.Id, BackupRun.TriggerScheduled, DateTime.UtcNow);
        await _backupRuns.AddAsync(run);
        await _unitOfWork.SaveChangesAsync();

        try
        {
            var result = await _backupService.CreateBackupAsync(destinationFolder: null);

            run.MarkSucceeded(
                result.DestinationPath, result.SizeBytes, result.VerifiedObjectCount, DateTime.UtcNow);
            await _backupRuns.UpdateAsync(run);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation(
                "Scheduled backup for clinic {ClinicId} succeeded: {Path} ({Objects} objects).",
                clinic.Id, result.DestinationPath, result.VerifiedObjectCount);

            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                _logger.LogWarning(
                    "Scheduled backup for clinic {ClinicId} carries a warning: {Warning}", clinic.Id, result.Warning);
            }

            // Prune only after a SUCCESS. Pruning after a failure would delete a good old backup on the strength
            // of a new one that does not exist — retention turning into data loss, which is the one thing the
            // pruner must never be able to do.
            await PruneAsync(clinic);
        }
        catch (Exception ex)
        {
            // Every foreseeable failure arrives here as an InvalidOperationException carrying an operator-facing
            // French message (missing pg_dump, unwritable destination, disk full, unreadable dump). A genuine bug
            // is recorded too, with its type name rather than a raw stack trace — the row is read by a dentist.
            var reason = ex is InvalidOperationException
                ? ex.Message
                : $"Échec inattendu de la sauvegarde ({ex.GetType().Name}).";

            run.MarkFailed(reason, DateTime.UtcNow, _backupService.ResolveDestinationRoot(null));
            await _backupRuns.UpdateAsync(run);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogError(ex, "Scheduled backup for clinic {ClinicId} failed.", clinic.Id);
        }
    }

    private async Task PruneAsync(Clinic clinic)
    {
        try
        {
            var pruned = await _backupService.PruneOldBackupsAsync(
                destinationFolder: null, keepCount: clinic.BackupRetentionCount);
            if (pruned > 0)
            {
                _logger.LogInformation(
                    "Pruned {Pruned} old backup folder(s) for clinic {ClinicId} (retention {Keep}).",
                    pruned, clinic.Id, clinic.BackupRetentionCount);
            }
        }
        catch (Exception ex)
        {
            // Swallowed: housekeeping must not turn a successful backup into a failed one.
            _logger.LogWarning(ex, "Pruning old backups failed for clinic {ClinicId}.", clinic.Id);
        }
    }

    /// <summary>
    /// Keeps the staleness alert equal to the truth — ensure when past the threshold, clear when not (L4d).
    /// </summary>
    private async Task EvaluateStalenessAsync(Clinic clinic)
    {
        var lastSuccess = await _backupRuns.GetLastSuccessfulAsync(clinic.Id);
        var threshold = DateTime.UtcNow.AddHours(-clinic.BackupStaleAfterHours);

        // ⚠️ A clinic that has NEVER backed up is measured from its creation, not from the epoch. Without that,
        // the alert fires on a clinic created five minutes ago — on the first screen a new owner ever sees, about
        // a backup they have not had time to configure — and an alert that is wrong on day one is an alert that
        // gets dismissed for ever.
        var reference = lastSuccess?.CompletedAt ?? lastSuccess?.StartedAt ?? clinic.CreatedAt;

        if (reference < threshold)
        {
            await _notificationGenerator.EnsureBackupStaleAsync(
                clinic.Id, lastSuccess?.CompletedAt ?? lastSuccess?.StartedAt, clinic.BackupStaleAfterHours);
        }
        else
        {
            await _notificationGenerator.ClearBackupStaleAsync(clinic.Id);
        }
    }
}
