namespace ClinicManagement.Domain.Enums;

/// <summary>
/// What an archive carries (<c>clinic-recovery-points</c>).
///
/// <para>⚠️ <b>It is recorded in the manifest, and that is load-bearing rather than bookkeeping.</b> A rows-only
/// archive and a full archive whose every blob failed to read are <i>byte-indistinguishable</i> without it — the
/// packager treats an unreadable blob as a warning, so both produce <c>BlobCount = 0</c> — and both would restore
/// reporting « 0 fichier ». « Cette archive ne contient pas les fichiers » and « les fichiers n'ont pas pu être
/// lus » are opposite facts with the same picture, and the second is the one that must send somebody looking at
/// the object store.</para>
///
/// <para><see cref="RowsAndFiles"/> is <b>0 on purpose</b>: a manifest written before this enum existed carries no
/// such field, so it deserialises to the default — and every archive written before it did carry its files. Adding
/// the field therefore needs no <c>SchemaVersion</c> bump, which
/// <c>ClinicArchiveFormat.SchemaVersion</c> reserves for a change in what an entry <i>means</i>.</para>
/// </summary>
public enum ClinicArchiveContents
{
    /// <summary>
    /// Every row and every blob behind them — what « Télécharger l'archive » produces, and the only kind that is a
    /// complete copy of the practice.
    /// </summary>
    RowsAndFiles = 0,

    /// <summary>
    /// The rows alone, no <c>blobs/</c> entries at all — what the daily recovery point produces.
    ///
    /// <para><b>Why the scheduled copy omits them.</b> A cabinet's rows are megabytes of JSON; its radiographs are
    /// gigabytes. Seven daily full copies would be seven copies of the whole object store per practice, which is
    /// not a cost a deployment can carry — and it buys little: a row deleted by mistake is the case recovery points
    /// exist for, while a blob's durability is the object store's own problem and is not improved by copying it
    /// into the same store. The full archive remains the manual download.</para>
    /// </summary>
    RowsOnly = 1
}
