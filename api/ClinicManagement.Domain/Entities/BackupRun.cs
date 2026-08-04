using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One backup attempt, recorded (L4d).
///
/// <para><b>Why a table.</b> Before it, the result of a backup lived in a React <c>useState</c>: there were zero
/// repo hits for <c>LastBackup</c> or <c>BackupHistory</c>, so « quand la dernière sauvegarde a-t-elle réussi ? »
/// — the only question that matters about a backup — could not be answered by the product at all, and closing the
/// browser tab erased the answer. A staleness alert is impossible without it, and a staleness alert is what turns
/// backup from a habit into a guarantee.</para>
///
/// <para><b>Every attempt, not every success.</b> A failed row is the more valuable one: it is what distinguishes
/// « nobody has backed up since Tuesday » from « it has been trying every night and failing », which are two
/// entirely different conversations with the practice.</para>
///
/// <para>⚠️ It is an <see cref="AggregateRoot{TId}"/> and therefore audited by
/// <c>AuditSaveChangesInterceptor</c> — which is correct: an admin changing the schedule or a run being recorded
/// are both things an owner may need to reconstruct. Unlike <c>Notification</c> (excluded from the audit for
/// being minutely machine noise) this writes at most a handful of rows a day.</para>
/// </summary>
public class BackupRun : AggregateRoot<Guid>
{
    /// <summary>How the run was triggered — the two values the product can produce.</summary>
    public const string TriggerScheduled = "Scheduled";
    public const string TriggerManual = "Manual";

    /// <summary>
    /// The clinic whose schedule asked for it (or whose admin clicked). Non-nullable: unlike an audit row, a
    /// backup is always attributable to a clinic — the scheduled job iterates clinics and the manual path runs as
    /// a clinic's admin.
    /// </summary>
    public Guid ClinicId { get; private set; }

    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public BackupOutcome Outcome { get; private set; }

    /// <summary>Where it was written. Recorded even on failure: « il essaie d'écrire sur D: qui n'existe plus ».</summary>
    public string? DestinationPath { get; private set; }

    public long? SizeBytes { get; private set; }

    /// <summary>
    /// How many objects <c>pg_restore --list</c> found in the dump (L4c). The number, not just a boolean: three
    /// tables where the schema has thirty-eight is a detectable disaster that a « ça a marché » cannot see.
    /// </summary>
    public int? VerifiedObjectCount { get; private set; }

    /// <summary>The operator-facing French reason, on a failed run only.</summary>
    public string? Error { get; private set; }

    public string Trigger { get; private set; } = TriggerScheduled;

    private BackupRun() { } // EF Core

    public BackupRun(Guid id, Guid clinicId, string trigger, DateTime startedAtUtc)
    {
        Id = id;
        ClinicId = clinicId;
        Trigger = trigger == TriggerManual ? TriggerManual : TriggerScheduled;
        StartedAt = startedAtUtc;
        Outcome = BackupOutcome.Running;
    }

    /// <summary>
    /// Marks the run successful. <paramref name="verifiedObjectCount"/> is required rather than optional
    /// because a run that was not verified is not a success (L4c): <c>pg_dump</c> exiting 0 is not proof, and the
    /// only way to keep that true is to make the proof impossible to omit at the call site.
    /// </summary>
    public void MarkSucceeded(string destinationPath, long sizeBytes, int verifiedObjectCount, DateTime completedAtUtc)
    {
        DestinationPath = destinationPath;
        SizeBytes = sizeBytes;
        VerifiedObjectCount = verifiedObjectCount;
        CompletedAt = completedAtUtc;
        Outcome = BackupOutcome.Succeeded;
        Error = null;
    }

    /// <summary>Marks the run failed, keeping the reason and the destination it was aiming at.</summary>
    public void MarkFailed(string error, DateTime completedAtUtc, string? destinationPath = null)
    {
        Error = string.IsNullOrWhiteSpace(error) ? "Échec de la sauvegarde." : error;
        DestinationPath = destinationPath ?? DestinationPath;
        CompletedAt = completedAtUtc;
        Outcome = BackupOutcome.Failed;
    }
}
