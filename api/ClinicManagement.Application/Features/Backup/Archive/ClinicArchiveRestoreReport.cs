namespace ClinicManagement.Application.Features.Backup.Archive;

/// <summary>
/// What a restore did, per table — the response body of both restore endpoints.
///
/// <para><b>Three counts and not one, because they are three different facts</b> and the middle one is what makes
/// a restore safe to run twice. « Restauré » is a row that was gone and is back; « déjà présent » is a row that
/// was already there, identical, and was not touched — so a second restore reports everything in this column and
/// changes nothing (AC-2). « Conflit » is a row that exists and <i>differs</i>: it was skipped, never overwritten,
/// because work done since the archive was taken must survive putting the archive back (AC-4).</para>
///
/// <para>Keyed by entity name rather than totalled, because « 3 conflits » says nothing an owner can act on while
/// « 3 conflits sur Patient » sends them to three patient records.</para>
/// </summary>
public sealed record ClinicArchiveRestoreReport
{
    /// <summary>When the archive was taken — the answer to « quelle sauvegarde ai-je remise ? ».</summary>
    public DateTime ArchivedAtUtc { get; init; }

    /// <summary>The cabinet the rows landed in. On the console path this is the cabinet that was just re-created.</summary>
    public Guid ClinicId { get; init; }

    /// <summary>Rows that were missing and are back, per entity.</summary>
    public IReadOnlyDictionary<string, int> Restored { get; init; } = new Dictionary<string, int>();

    /// <summary>Rows that were already there and identical, per entity. Nothing was written for these.</summary>
    public IReadOnlyDictionary<string, int> AlreadyPresent { get; init; } = new Dictionary<string, int>();

    /// <summary>Rows that exist but differ from the archive, per entity. <b>Skipped</b>, never overwritten.</summary>
    public IReadOnlyDictionary<string, int> Conflicts { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// The French name of each entity the three dictionaries are keyed on.
    ///
    /// <para><b>The keys stay the English CLR names and the label travels beside them</b> — the repo's standing
    /// convention (<c>appointment-labels.ts</c>, <c>invoice-labels.ts</c>, <c>AuditLabels</c>). Before it, the
    /// screen printed the key: a French cabinet owner read « PatientMedicalHistory · 12 remis » and
    /// « InstallmentPayment · 3 ignorés » at the moment they were most anxious, and the list sorted by an
    /// identifier they could not predict. An entity with no mapping keeps its own name rather than a placeholder.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string> EntityLabels { get; init; } = new Dictionary<string, string>();

    /// <summary>Blobs written back at their original storage keys.</summary>
    public int BlobsRestored { get; init; }

    /// <summary>What could not be restored, in French. Empty is the ordinary case.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Total rows re-inserted — the headline the screen leads with.</summary>
    public int TotalRestored => Restored.Values.Sum();

    /// <summary>Total rows left untouched because they were already present and identical.</summary>
    public int TotalAlreadyPresent => AlreadyPresent.Values.Sum();

    /// <summary>Total rows skipped because the live version differs.</summary>
    public int TotalConflicts => Conflicts.Values.Sum();
}
