namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// One line of the cabinet-wide file manifest (<c>patient-file-mirror</c>) — what a machine keeping a browsable
/// copy needs in order to decide whether it already holds this file, and nothing more.
///
/// <para>⚠️ <b>No <c>StorageKey</c>, deliberately.</b> The manifest says which files exist; fetching one goes
/// through the per-patient download, which re-checks the patient's own clinic. Handing out the blob key would
/// make the manifest a second, unguarded way to name an object in the store — the exact shape US-5 closed when it
/// required a <c>Guid clinicId</c> on every <c>UploadAsync</c>.</para>
///
/// <para>⚠️ <see cref="PatientName"/> is joined here rather than looked up per row by the caller: a cabinet with
/// forty thousand files would otherwise issue forty thousand patient reads to build one tree.</para>
/// </summary>
public sealed record ClinicFileManifestRow(
    Guid FileId,
    Guid PatientId,
    string PatientName,
    string FileName,
    string ContentType,
    long FileSize,
    DateTime UploadedAt);
