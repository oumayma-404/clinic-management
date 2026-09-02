using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Files;

/// <summary>
/// The objects in the store a <see cref="PatientFile"/> row owns — the one answer to « what goes when this row
/// goes? ».
///
/// <para>⚠️ <b>A row owns up to two blobs, and the second one was missed for both delete paths.</b> The original
/// is owned only when <see cref="FileResidency.Hosted"/>: a coffre file's bytes are on the practice's own disk,
/// under a ten-to-twenty-year retention duty, and the app never destroys what it does not host. But the
/// <see cref="PatientFile.PreviewStorageKey"/> stand-in <i>is</i> hosted — it is the one part of a coffre file the
/// deployment stores — so it is ours to delete and ours to archive. Both delete commands branched on residency for
/// the original and never named the preview, which would leave one orphan per deleted study, invisibly, forever.</para>
///
/// <para>⚠️ <b>Answer here, never at a call site.</b> Two callers already read this wrong in two different files;
/// a third would read it wrong in a third. <c>BlobLifecycleCoverageTests</c> is the derived guard that fails the
/// build when a new <c>…StorageKey</c> property appears on an entity and nothing here or in
/// <c>ClinicArchiveScope.BlobProperties</c> accounts for it.</para>
/// </summary>
public static class PatientFileBlobs
{
    /// <summary>
    /// Every storage key this row owns, in delete order (original first, then its stand-in). Empty entries are
    /// dropped, so a hosted row with no preview yields one key and a coffre row yields at most one.
    /// </summary>
    public static IEnumerable<string> OwnedBy(PatientFile file)
    {
        if (file.Residency == FileResidency.Hosted && !string.IsNullOrWhiteSpace(file.StorageKey))
        {
            yield return file.StorageKey!;
        }

        if (!string.IsNullOrWhiteSpace(file.PreviewStorageKey))
        {
            yield return file.PreviewStorageKey!;
        }
    }

    /// <summary>
    /// The keys owned by a set of rows — the folder-delete shape.
    ///
    /// <para>⚠️ <b>Not de-duplicated, deliberately.</b> Every key is minted per upload
    /// (<c>ClinicStorageKey.Compose</c>) or derived from the row's own id (the preview), so two rows sharing one
    /// object is a data anomaly rather than a case; and both backends' <c>DeleteAsync</c> is idempotent, so the
    /// cost of the anomaly is a redundant call. Collapsing here would instead mean one row's blob silently
    /// standing in for another's, which is the failure worth avoiding.</para>
    /// </summary>
    public static IEnumerable<string> OwnedByAll(IEnumerable<PatientFile> files) =>
        files.SelectMany(OwnedBy);
}
