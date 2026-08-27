using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One retained, restorable copy of a cabinet's rows, taken unattended (<c>clinic-recovery-points</c>).
///
/// <para><b>Why it exists.</b> The per-clinic archive was a complete recovery mechanism with nothing producing an
/// archive: no schedule, no reminder, and nothing anywhere recording when one was last taken. Recovery therefore
/// depended on whether somebody had happened to press a button — and every delete in this product is a hard delete
/// (there is no soft delete, no trash and no undo anywhere in the domain), so « j'ai supprimé la fiche » had no
/// answer at all unless a file existed from before it. This is the row that makes « restaurer depuis le point du
/// 12/08 » possible.</para>
///
/// <para><b>Every attempt, not every success</b> — <see cref="BackupRun"/>'s reasoning verbatim. A failed row is the
/// more valuable one: it distinguishes « personne n'a de point de restauration » from « il essaie chaque nuit et il
/// échoue », which are two entirely different conversations. And the <c>Running</c> row is committed <i>before</i>
/// the work starts, so a crash leaves a visible row rather than none.</para>
///
/// <para>⚠️ <b>It must be excluded from the archive</b> (<c>ClinicArchiveScope.Excluded</c>), beside
/// <see cref="BackupRun"/> and for a sharper reason than transience: these rows name <b>storage keys</b>, so an
/// archive carrying them would restore a list of recovery points whose objects may have been pruned since —
/// offering a practice a recovery that cannot be performed, which is worse than offering none.</para>
///
/// <para>⚠️ An <see cref="AggregateRoot{TId}"/> and therefore audited by <c>AuditSaveChangesInterceptor</c>, which
/// is correct and cheap: this writes a couple of rows per clinic per day, unlike <c>Notification</c> (excluded for
/// being minutely machine noise). « Qui a restauré, et depuis quel point ? » is a question an owner may need to
/// reconstruct.</para>
/// </summary>
public class ClinicRecoveryPoint : AggregateRoot<Guid>
{
    /// <summary>
    /// How many points a cabinet keeps before the oldest is pruned.
    ///
    /// <para>⚠️ <b>A constant and not a per-clinic setting</b>, deliberately. The only endpoint that could edit one
    /// is <c>PUT /api/backup/schedule</c>, which <b>404s where <c>BacksUpItsOwnData</c> is false</b> — that is,
    /// on the hosted deployment this feature matters most on. A setting nobody there can reach is worse than a
    /// stated default: it reads as configurable and is not.</para>
    /// </summary>
    public const int RetentionCount = 7;

    /// <summary>
    /// After how many days without a <b>downloaded</b> archive the administrators are told
    /// (<see cref="Clinic.LastArchiveDownloadedAtUtc"/>).
    ///
    /// <para>Thirty days, and a constant for <see cref="RetentionCount"/>'s reason. It is deliberately <i>not</i>
    /// about these rows: a recovery point sitting inside the deployment dies with the deployment, so the fact worth
    /// nagging about is the copy that left the building.</para>
    /// </summary>
    public const int ArchiveStaleAfterDays = 30;

    /// <summary>The cabinet this point belongs to. Non-nullable: the pass iterates clinics.</summary>
    public Guid ClinicId { get; private set; }

    public DateTime StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public BackupOutcome Outcome { get; private set; }

    /// <summary>
    /// What the archive carries. Recorded on the row as well as in the manifest, so the list can say « lignes
    /// seulement » without opening the file.
    /// </summary>
    public ClinicArchiveContents Contents { get; private set; }

    /// <summary>
    /// Where the archive landed, through <c>IFileStorage</c> — so it is a tenant-prefixed
    /// <c>clinics/{clinicId}/recovery-points/…</c> key. Null until the point succeeds, and on a failed one.
    /// </summary>
    public string? StorageKey { get; private set; }

    public long? SizeBytes { get; private set; }

    /// <summary>
    /// How many tables and rows the manifest declared. The <b>row count</b> is the figure worth keeping: « 3 tables »
    /// where the cabinet has forty is a detectable disaster that a size in bytes cannot express, and a point whose
    /// row count collapsed overnight is the one an owner must not restore blindly.
    /// </summary>
    public int? TableCount { get; private set; }

    public int? RowCount { get; private set; }

    /// <summary>The operator-facing French reason, on a failed point only.</summary>
    public string? Error { get; private set; }

    private ClinicRecoveryPoint() { } // For EF Core

    public ClinicRecoveryPoint(Guid id, Guid clinicId, ClinicArchiveContents contents, DateTime startedAtUtc)
    {
        Id = id;
        ClinicId = clinicId;
        Contents = contents;
        StartedAt = startedAtUtc;
        Outcome = BackupOutcome.Running;
    }

    /// <summary>
    /// Marks the point usable. <paramref name="rowCount"/> is required rather than optional for
    /// <c>BackupRun.MarkSucceeded</c>'s reason: a point nobody counted is not a point anybody should restore from,
    /// and the only way to keep that true is to make the figure impossible to omit at the call site.
    /// </summary>
    public void MarkSucceeded(
        string storageKey, long sizeBytes, int tableCount, int rowCount, DateTime completedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Un point de restauration doit nommer son emplacement.", nameof(storageKey));
        }

        StorageKey = storageKey;
        SizeBytes = sizeBytes;
        TableCount = tableCount;
        RowCount = rowCount;
        CompletedAt = completedAtUtc;
        Outcome = BackupOutcome.Succeeded;
        Error = null;
    }

    /// <summary>Marks the attempt failed, keeping the reason a dentist will read.</summary>
    public void MarkFailed(string error, DateTime completedAtUtc)
    {
        Error = string.IsNullOrWhiteSpace(error)
            ? "Échec de la création du point de restauration."
            : error;
        CompletedAt = completedAtUtc;
        Outcome = BackupOutcome.Failed;
    }

    /// <summary>
    /// Whether this point can be restored from — a success that still names an object.
    ///
    /// <para>Asked by the restore command rather than re-derived there, so « restaurable » has one definition: a
    /// <c>Running</c> row left by a crash and a <c>Failed</c> one are both refusals, and so is a success whose key
    /// is somehow absent.</para>
    /// </summary>
    public bool IsRestorable => Outcome == BackupOutcome.Succeeded && !string.IsNullOrWhiteSpace(StorageKey);
}
