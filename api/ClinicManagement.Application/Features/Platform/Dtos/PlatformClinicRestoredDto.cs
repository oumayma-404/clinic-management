using ClinicManagement.Application.Features.Backup.Archive;

namespace ClinicManagement.Application.Features.Platform.Dtos;

/// <summary>
/// What the console gets back after re-creating a cabinet from an archive
/// (<c>clinic-data-archive-and-restore</c>).
///
/// <para>⚠️ <b><see cref="OneTimePassword"/> is shown exactly once</b>, the shape
/// <c>platform-account create</c> and <c>provision-clinic</c> already use: it is not stored anywhere in readable
/// form and the account it belongs to must change it on first use. The vendor reads it to the practice over the
/// phone; there is no second chance and the screen says so.</para>
///
/// <para>⚠️ <b>The restore's per-entity counts are projected into <see cref="Tables"/> rather than carrying
/// <see cref="ClinicArchiveRestoreReport"/> itself</b>, and that is US-7's doing rather than taste. The report keys
/// its three counts by entity name in a <c>Dictionary</c>, whose leaves reflect as <c>Key</c> and <c>Value</c> —
/// and <c>PlatformReadShape</c> is a closed set of <i>names</i>, so allowing those two would pre-approve every
/// future dictionary on this surface, including one whose values are patient names. A named row carries the same
/// information and stays reviewable. The cabinet's own endpoint returns the full report unchanged.</para>
/// </summary>
public sealed record PlatformClinicRestoredDto
{
    /// <summary>The cabinet's own id, taken from the archive rather than minted — which is what makes every restored row point at it.</summary>
    public Guid ClinicId { get; init; }

    /// <summary>The practice's name as the archive recorded it.</summary>
    public string ClinicName { get; init; } = string.Empty;

    /// <summary>The administrator account created for the restored cabinet.</summary>
    public string AdminEmail { get; init; } = string.Empty;

    /// <summary>Shown once. See the type's remarks.</summary>
    public string OneTimePassword { get; init; } = string.Empty;

    /// <summary>When the archive was taken — the answer to « quelle sauvegarde ai-je remise ? ».</summary>
    public DateTime ArchivedAtUtc { get; init; }

    /// <summary>What the restore did, one row per entity type. Empty where the archive carried nothing.</summary>
    public IReadOnlyList<PlatformRestoredTableDto> Tables { get; init; } = Array.Empty<PlatformRestoredTableDto>();

    /// <summary>Blobs written back at their original storage keys.</summary>
    public int BlobsRestored { get; init; }

    /// <summary>What could not be restored, in French. Empty is the ordinary case.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Projects the report both doors produce onto the console's own shape — every entity the restore touched in
    /// any of the three ways, so a table that was wholly « déjà présent » is still listed rather than absent.
    /// </summary>
    public static IReadOnlyList<PlatformRestoredTableDto> TablesOf(ClinicArchiveRestoreReport report) =>
        report.Restored.Keys
            .Concat(report.AlreadyPresent.Keys)
            .Concat(report.Conflicts.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entity => entity, StringComparer.Ordinal)
            .Select(entity => new PlatformRestoredTableDto
            {
                Entity = entity,
                EntityLabel = report.EntityLabels.GetValueOrDefault(entity, entity),
                Restored = report.Restored.GetValueOrDefault(entity),
                AlreadyPresent = report.AlreadyPresent.GetValueOrDefault(entity),
                Conflicts = report.Conflicts.GetValueOrDefault(entity),
            })
            .OrderBy(row => row.EntityLabel, StringComparer.CurrentCulture)
            .ToList();
}

/// <summary>
/// One entity type's outcome. The three counts are three different facts and are never totalled here: « restauré »
/// was gone and is back, « déjà présent » was there and identical, « conflit » exists and <i>differs</i> and was
/// skipped rather than overwritten (AC-4).
/// </summary>
public sealed record PlatformRestoredTableDto
{
    /// <summary>The entity type's own name — a table, never anything a practice records in one.</summary>
    public string Entity { get; init; } = string.Empty;

    /// <summary>
    /// Its French name, from <c>AuditLabels</c>. The key stays on the wire and the label travels beside it — the
    /// standing convention — because the console screen otherwise printed « PatientMedicalHistory » and sorted
    /// the rows by an identifier no reader can predict. An unmapped type keeps its own name.
    /// </summary>
    public string EntityLabel { get; init; } = string.Empty;

    /// <summary>Rows that were missing and are back.</summary>
    public int Restored { get; init; }

    /// <summary>Rows that were already there and identical. Nothing was written for these.</summary>
    public int AlreadyPresent { get; init; }

    /// <summary>Rows that exist but differ from the archive. Skipped, never overwritten.</summary>
    public int Conflicts { get; init; }
}
