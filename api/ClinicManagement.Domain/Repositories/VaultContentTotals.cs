namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// What a cabinet's coffre holds according to the <b>records</b> — the yardstick a copy report is measured
/// against.
///
/// <para>⚠️ A coffre original never reached the deployment, so nothing on the server can observe whether the
/// practice's second copy is real or complete. These two figures are the closest thing to evidence there is: the
/// shell says how many files and how many bytes it copied, and this says how many there were to copy. A shortfall
/// is the difference between « sauvegardé » and « on a copié trois études sur quatre cents ».</para>
/// </summary>
/// <param name="FileCount">How many rows are filed in the coffre.</param>
/// <param name="TotalBytes">Their summed size, as each row recorded it at registration.</param>
public readonly record struct VaultContentTotals(int FileCount, long TotalBytes)
{
    public static readonly VaultContentTotals Empty = new(0, 0);

    /// <summary>Nothing to copy, so nothing to warn about — the « is there anything to lose? » test.</summary>
    public bool IsEmpty => FileCount == 0;

    /// <summary>
    /// Whether a report of <paramref name="reportedFiles"/> files and <paramref name="reportedBytes"/> bytes
    /// accounts for everything on record.
    ///
    /// <para>⚠️ Deliberately « at least », not « exactly ». The coffre is a folder on the practice's own machine
    /// and may legitimately hold more than the app filed there — an older export, a folder somebody copied in —
    /// so a copy larger than the record is complete, not suspicious. Only a shortfall means something was left
    /// behind.</para>
    /// </summary>
    public bool IsCoveredBy(int reportedFiles, long reportedBytes) =>
        reportedFiles >= FileCount && reportedBytes >= TotalBytes;
}
