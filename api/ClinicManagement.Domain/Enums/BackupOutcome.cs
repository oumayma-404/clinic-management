namespace ClinicManagement.Domain.Enums;

/// <summary>
/// How one backup attempt ended (L4d).
///
/// <para>Three values and no « Skipped », deliberately. A destination on a removable drive that is absent at
/// 02:00 records a <see cref="Failed"/> run, because in a history list a skipped run is indistinguishable from a
/// successful one — and « rien ce soir-là » is exactly the reading that loses a practice its data.</para>
/// </summary>
public enum BackupOutcome
{
    /// <summary>Started and not yet resolved. A row left here is a crash mid-backup, which is itself a finding.</summary>
    Running = 1,

    /// <summary>Dumped, copied <b>and verified readable</b> (L4c) — the only value that resets the staleness clock.</summary>
    Succeeded = 2,

    /// <summary>Did not complete. The reason is on the row; the partial folder was removed.</summary>
    Failed = 3
}
