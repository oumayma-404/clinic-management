using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.API.BackgroundJobs;

/// <summary>
/// The daily recovery point (<c>clinic-recovery-points</c>) — the job that turns « quelqu'un se souvient de
/// télécharger une archive » into a guarantee. Per cabinet: build today's point if it is due, prune old ones to
/// <see cref="ClinicRecoveryPoint.RetentionCount"/>, and keep the archive-staleness alert in step with reality.
///
/// <para><b>Registered on every deployment kind, and it asks no capability.</b> Unlike <see cref="BackupJob"/> — which
/// runs <c>pg_dump</c> and is therefore <c>BacksUpItsOwnData</c>-only — this goes through the tenant filter like every
/// CSV export and carries one cabinet's rows. On <c>SelfHostedLan</c> it is strictly better than what exists there
/// too: the only recovery today is the <c>restore-backup</c> verb, which stops the app and restores the *whole*
/// database to undo one deleted fiche.</para>
///
/// <para><b>Deliberately not connectivity-gated</b>, for <see cref="StockExpiryJob"/>'s reason: the output is a
/// database row and an object in the deployment's own store, so it must work on an offline LAN install.</para>
///
/// <para>⚠️ <b>Rows only, no blobs.</b> A cabinet's rows are megabytes of JSON; its radiographs are gigabytes, and
/// seven daily full copies would be seven copies of the object store per practice. A row deleted by mistake is the
/// case this exists for; a blob's durability is the object store's own problem and is not improved by copying it into
/// the same store. The full archive stays the manual download — which is what the staleness alert nags for.</para>
///
/// <para>⚠️ <b>It cannot send <c>BuildClinicArchiveQuery</c>.</b> That resolves the cabinet from
/// <c>IClinicContext.GetUserId()</c> and re-checks <c>IsAdmin()</c>; a job has no caller and no token. It calls the
/// packager directly, exactly as that query does, with the clinic id it is iterating.</para>
/// </summary>
public class ClinicRecoveryPointJob
{
    /// <summary>
    /// After a failed attempt, how long before this job tries that cabinet again — <see cref="BackupJob"/>'s bound and
    /// for its reason: without it a cabinet whose object store is unreachable would write a failure row on every run,
    /// and a list nobody can read is the same as no list.
    /// </summary>
    private const int RetryQuietHours = 6;

    /// <summary>The clinic-local hour a point is taken at. Fixed, unlike the backup's per-clinic hour, because
    /// nothing about this is visible to the practice while it runs and there is nothing to schedule around.</summary>
    private const int HourLocal = 3;

    private readonly IClinicRepository _clinicRepository;
    private readonly IClinicRecoveryPointRepository _points;
    private readonly IClinicArchiveStore _store;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IPatientFileRepository _patientFiles;
    private readonly IAuditActorProvider _auditActor;
    private readonly ITenantScope _tenantScope;
    private readonly ILogger<ClinicRecoveryPointJob> _logger;

    public ClinicRecoveryPointJob(
        IClinicRepository clinicRepository,
        IClinicRecoveryPointRepository points,
        IClinicArchiveStore store,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator,
        IPatientFileRepository patientFiles,
        IAuditActorProvider auditActor,
        ITenantScope tenantScope,
        ILogger<ClinicRecoveryPointJob> logger)
    {
        _clinicRepository = clinicRepository;
        _points = points;
        _store = store;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
        _patientFiles = patientFiles;
        _auditActor = auditActor;
        _tenantScope = tenantScope;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    [AutomaticRetry(Attempts = 0)]
    public async Task TakeRecoveryPoints()
    {
        // I6: a job has no token, so without naming itself every row it writes reads « Tâche automatique » with no
        // clue which one.
        _auditActor.RunAs(nameof(ClinicRecoveryPointJob));

        // US-2: ClinicRecoveryPoint is clinic-filtered, so the due-check, the prune and the staleness alert all need
        // every cabinet's rows. Unscoped, every cabinet would read as having no point and get a new one every tick.
        _tenantScope.UseSystemWide("ClinicRecoveryPointJob takes and prunes recovery points for every clinic");

        var clinics = await _clinicRepository.GetAllAsync();

        foreach (var clinic in clinics)
        {
            // One try/catch per cabinet: one practice's object store being unreachable must not cost every other
            // practice its recovery point.
            try
            {
                await ProcessClinicAsync(clinic);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recovery point failed for clinic {ClinicId}", clinic.Id);
            }
        }
    }

    private async Task ProcessClinicAsync(Clinic clinic)
    {
        if (await IsDueAsync(clinic))
        {
            await TakePointAsync(clinic);
        }

        // Evaluated whether or not a point was taken, and whether or not one succeeded: this alert is about the copy
        // the *practice* holds, which nothing this job does can change.
        await EvaluateArchiveStalenessAsync(clinic);

        // Its own question, and never folded into the one above: the archive carries the rows and the hosted blobs,
        // while a coffre original was never on the server — so a cabinet can hold a perfectly fresh archive and a
        // coffre nobody has ever copied. That is exactly the state where a failed disk loses a decade of imaging
        // while every backup indicator reads green.
        await EvaluateVaultCopyStalenessAsync(clinic);
    }

    /// <summary>
    /// Two questions, in the order that costs least: has a point already succeeded in this cabinet's own day, and has
    /// an attempt just failed?
    ///
    /// <para>The hour is checked last for <see cref="BackupJob.IsDueAsync"/>'s reason — <c>&gt;=</c> and not
    /// <c>==</c>, so a deployment that was down at 03:00 is caught the first hour it is up rather than skipping the
    /// day silently. Hangfire fires this daily, but a missed occurrence runs late.</para>
    /// </summary>
    private async Task<bool> IsDueAsync(Clinic clinic)
    {
        var startOfDay = ClinicClock.StartOfLocalDayUtc(ClinicClock.ClinicToday());

        var latest = await _points.GetLatestAsync(clinic.Id);

        // Already succeeded in this cabinet's own day → done. « Today » is the clinic's day, not UTC's: a 03:00 Tunis
        // point is 02:00 UTC, so a UTC boundary would file it under the previous day and the check would fire twice.
        if (latest != null
            && latest.Outcome == BackupOutcome.Succeeded
            && latest.StartedAt >= startOfDay)
        {
            return false;
        }

        // An attempt inside the quiet window → wait. Covers a recent failure and a Running row left by a crash,
        // which must not be joined by a second build while the first may still be writing.
        if (latest != null && latest.StartedAt > DateTime.UtcNow.AddHours(-RetryQuietHours))
        {
            return false;
        }

        return ClinicClock.ToClinicLocal(DateTime.UtcNow).Hour >= HourLocal;
    }

    /// <summary>
    /// One attempt, recorded whatever happens.
    ///
    /// <para>⚠️ The <c>Running</c> row is committed <b>before</b> the archive is built, exactly as
    /// <see cref="BackupJob"/> does: a crash mid-build then leaves a visible row instead of no row at all, and
    /// « rien cette nuit-là » is the reading that loses a practice its data. It also serialises the quiet-window
    /// check above against a run that dies without unwinding.</para>
    /// </summary>
    private async Task TakePointAsync(Clinic clinic)
    {
        // ⚠️ The sequence itself lives in `ClinicRecoveryPointWriter`, shared with « Annuler cet import », which
        // takes a point before it deletes anything. Three details in it are easy to get subtly wrong — the
        // `Running` row committed BEFORE the build, the clinic id passed rather than read off the ambient scope,
        // and the seekable self-deleting buffer — and a second copy of them is the `fixes-dont-propagate` shape
        // this repository keeps finding. The writer records the attempt and never throws.
        var point = await ClinicRecoveryPointWriter.TakeAsync(
            clinic, _points, _store, _fileStorage, _unitOfWork, _logger);

        // Prune only after a SUCCESS. Pruning after a failure would delete a good old point on the strength of a
        // new one that does not exist — retention turning into data loss, which is the one thing it must never do.
        if (point.IsRestorable)
        {
            await PruneAsync(clinic);
        }
    }

    /// <summary>
    /// Drops the oldest succeeded points beyond the retention count, and their objects.
    ///
    /// <para>⚠️ <b>The object is deleted before the row, and a failed delete keeps the row.</b> The other order leaks
    /// an object nothing points at — invisible, and it accumulates for the life of the deployment. This order can at
    /// worst leave a row whose object is already gone, which the restore names as a refusal rather than meeting as a
    /// crash.</para>
    /// </summary>
    private async Task PruneAsync(Clinic clinic)
    {
        try
        {
            var prunable = await _points.GetPrunableAsync(clinic.Id, ClinicRecoveryPoint.RetentionCount);

            foreach (var old in prunable)
            {
                if (!string.IsNullOrWhiteSpace(old.StorageKey))
                {
                    await _fileStorage.DeleteAsync(old.StorageKey);
                }

                await _points.RemoveAsync(old);
            }

            if (prunable.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation(
                    "Pruned {Count} old recovery point(s) for clinic {ClinicId} (retention {Keep}).",
                    prunable.Count, clinic.Id, ClinicRecoveryPoint.RetentionCount);
            }
        }
        catch (Exception ex)
        {
            // Swallowed: housekeeping must not turn a successful point into a failed one.
            _logger.LogWarning(ex, "Pruning old recovery points failed for clinic {ClinicId}.", clinic.Id);
        }
    }

    /// <summary>
    /// Keeps the « archive ancienne » alert equal to the truth — ensure past the threshold, clear when not.
    ///
    /// <para>⚠️ A cabinet that has <b>never</b> exported is measured from its own creation, not from the epoch. Without
    /// that the alert fires on a practice created five minutes ago, on the first screen a new owner ever sees, about
    /// something they have not had time to do — and an alert that is wrong on day one is dismissed for ever
    /// (<see cref="BackupJob"/>'s own trap, one field over).</para>
    /// </summary>
    private async Task EvaluateArchiveStalenessAsync(Clinic clinic)
    {
        var threshold = DateTime.UtcNow.AddDays(-ClinicRecoveryPoint.ArchiveStaleAfterDays);
        var reference = clinic.LastArchiveDownloadedAtUtc ?? clinic.CreatedAt;

        if (reference < threshold)
        {
            await _notificationGenerator.EnsureArchiveStaleAsync(
                clinic.Id, clinic.LastArchiveDownloadedAtUtc, ClinicRecoveryPoint.ArchiveStaleAfterDays);
        }
        else
        {
            await _notificationGenerator.ClearArchiveStaleAsync(clinic.Id);
        }
    }

    /// <summary>
    /// The same shape for the coffre, with one extra term in front of it: <b>is there anything to lose?</b>
    ///
    /// <para>⚠️ A cabinet whose coffre is empty is never nagged, and the count is asked <i>first</i> so the common
    /// case — most practices, most days, no vault files at all — costs one indexed count and no notification read.
    /// A warning about an empty folder is the fastest way to teach an owner to dismiss this alert, and the real one
    /// would go with it.</para>
    ///
    /// <para>⚠️ Measured from <see cref="Clinic.CreatedAt"/> when nothing has been reported, exactly as the archive's
    /// is: from the epoch it would fire the day the first study is filed, before anybody could have copied it.</para>
    /// </summary>
    private async Task EvaluateVaultCopyStalenessAsync(Clinic clinic)
    {
        if (await _patientFiles.CountVaultFilesAsync(clinic.Id) == 0)
        {
            await _notificationGenerator.ClearVaultCopyStaleAsync(clinic.Id);
            return;
        }

        var threshold = DateTime.UtcNow.AddDays(-ClinicRecoveryPoint.VaultCopyStaleAfterDays);
        var reference = clinic.LastVaultCopyAtUtc ?? clinic.CreatedAt;

        if (reference < threshold)
        {
            await _notificationGenerator.EnsureVaultCopyStaleAsync(
                clinic.Id, clinic.LastVaultCopyAtUtc, ClinicRecoveryPoint.VaultCopyStaleAfterDays);
        }
        else
        {
            await _notificationGenerator.ClearVaultCopyStaleAsync(clinic.Id);
        }
    }
}
