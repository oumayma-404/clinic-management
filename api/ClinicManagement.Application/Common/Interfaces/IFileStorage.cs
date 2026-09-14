namespace ClinicManagement.Application.Common.Interfaces;

public interface IFileStorage
{
    /// <summary>
    /// Stores a blob for <paramref name="clinicId"/> under a unique generated key, and returns that key.
    ///
    /// <para><b>Every upload names its clinic</b> (multi-tenant-cloud US-5): the backend prefixes the key with
    /// <c>clinics/{clinicId}/</c>, so a hosted deployment's object store is partitioned by tenant rather than a
    /// flat pile of guids. The id is a required parameter rather than something the backend reads off the ambient
    /// tenant scope, because the two uploads with no request behind them cannot supply one that way — the
    /// outbox jobs run <c>SystemWide</c> (no clinic in scope at all) and would silently write an
    /// unattributed key.</para>
    /// </summary>
    Task<string> UploadAsync(Stream file, string contentType, Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a blob at a deterministic path <b>within</b> the clinic (e.g. <c>logo</c>,
    /// <c>doctors/{id}/cachet</c>), overwriting in place, and returns the composed key.
    ///
    /// <para>⚠️ <paramref name="relativePath"/> must not carry a clinic segment of its own — the backend adds it.
    /// A path that climbs out of the prefix is refused.</para>
    /// </summary>
    Task<string> UploadAsync(Stream file, string contentType, Guid clinicId, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the blob stored under <paramref name="storageKey"/> — <b>verbatim</b>, with no prefixing.
    /// A row written before US-5 holds a flat key and must keep resolving with no backfill (amendment M2).
    ///
    /// <para>⚠️ <b>The stream is forward-only and is read as the caller consumes it.</b> Both backends used to
    /// copy the whole object into a <c>MemoryStream</c> first, so every concurrent download held the entire file
    /// in the server's memory — three people opening a 50 Mo panoramique was 150 Mo of a small VPS, and a
    /// hosted CBCT would be far worse. Nothing in this solution seeks a downloaded blob: every consumer copies
    /// it forward, and no download action enables range processing.</para>
    ///
    /// <para>⚠️ A missing object still throws <b>here</b>, not on the first read. The handlers around this turn
    /// an exception into a French <c>Result</c> failure, and a stream that fails once the response has begun is
    /// a 200 with a truncated body instead.</para>
    /// </summary>
    Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many bytes are stored under <paramref name="storageKey"/>, or null when nothing is there.
    ///
    /// <para>⚠️ <b>Asked of the store rather than read off the row, deliberately.</b> It is what lets a download
    /// still carry a <c>Content-Length</c> now that the stream is not seekable — and without it a browser
    /// downloading a study shows « unknown size » with no progress, which on a slow connection is exactly when
    /// somebody needs it. <c>PatientFile.FileSize</c> looks like the same number and is not safe to use: for a
    /// row written before upload validation existed it is the <i>client's claim</i>, and a wrong
    /// <c>Content-Length</c> truncates or hangs the response rather than merely misreporting.</para>
    /// </summary>
    Task<long?> GetLengthAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a blob back at <paramref name="storageKey"/> <b>verbatim</b>, restoring bytes that already had a key
    /// (<c>clinic-data-archive-and-restore</c> AC-5).
    ///
    /// <para>⚠️ <b>Deliberately not an <c>UploadAsync</c> overload, and the name is load-bearing.</b> US-5's
    /// guarantee is that « an unprefixed key is not something a caller can write », held by every <c>UploadAsync</c>
    /// requiring a clinic id — a property <c>ClinicStorageKeyTests</c> reflects off this interface rather than off a
    /// list, so a third upload overload taking no clinic would silently restore the defect US-5 closed. This is not
    /// an upload: it takes no clinic because it mints no key, it names the key a row <i>already</i> holds, and that
    /// key may legitimately be a flat pre-US-5 one (EC-4) which composing would move out from under its own row.</para>
    ///
    /// <para>It is the write-side mirror of <see cref="DownloadAsync"/>, and the asymmetry is the same one: new
    /// keys are composed, existing keys are honoured.</para>
    /// </summary>
    Task RestoreAtKeyAsync(
        Stream file,
        string contentType,
        string storageKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a blob already exists at <paramref name="storageKey"/>, verbatim.
    ///
    /// <para>What makes a restore's blob half additive like its rows: bytes that are already there are left alone,
    /// so a re-restore neither re-uploads nor overwrites a file the practice has since replaced.</para>
    /// </summary>
    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Removes the blob stored under <paramref name="storageKey"/>, verbatim — see <see cref="DownloadAsync"/>.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every blob this deployment holds for <paramref name="clinicId"/> — the whole
    /// <c>clinics/{clinicId}/</c> prefix — and returns how many objects were removed. Written for the console's
    /// « supprimer définitivement ce cabinet ».
    ///
    /// <para><b>A prefix sweep, not a key-by-key pass over the rows</b>, because the rows are the very thing being
    /// deleted beside it. Harvesting keys from the six columns that hold one would miss a logo whose row has
    /// already gone, the staged parts of an upload nobody finished (<c>clinics/{id}/uploads/…</c>, which
    /// <c>MinioResumableUploadStore</c> writes and no row names), and every blob a column added later holds. The
    /// prefix is the one authority on « which blobs belong to this cabinet », and <c>ClinicStorageKey</c> is what
    /// makes that true of every key written since US-5.</para>
    ///
    /// <para>⚠️ <b>A flat pre-US-5 key is outside the prefix and therefore survives</b> — amendment M2 again, and
    /// correct rather than merely tolerated: those rows exist only on a LAN install, where the practice owns the
    /// disk and there is no console to call this.</para>
    ///
    /// <para>⚠️ <b>Best effort by contract.</b> It runs after the rows are committed, so a blob it cannot remove is
    /// wasted bytes and not a wrong record — an implementation logs and carries on rather than throwing, and the
    /// count it returns is what it actually removed.</para>
    /// </summary>
    Task<int> DeleteByClinicAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the backend is reachable and usable — the bucket answers, or the base folder exists and is
    /// writable — <b>without</b> storing anything (multi-tenant-cloud US-6, the <c>/health</c> storage check).
    ///
    /// <para><b>Throws</b> on failure, carrying the backend's own message, rather than returning a bool: the
    /// health report renders that message, and « storage: false » would leave the operator exactly as blind as
    /// the 503 it produced. Returning normally means healthy.</para>
    ///
    /// <para><b>Why a probe rather than a round trip through the existing methods.</b> Upload → download →
    /// delete of a sentinel blob would prove more, but a container health check runs every few seconds for the
    /// life of the deployment: that is three storage operations per tick, for ever, writing into the clinic's own
    /// file store. Reachability is the failure this check exists to catch — a missing MinIO container, a
    /// credential that stopped working, an unmounted volume — and all three show up here.</para>
    /// </summary>
    Task ProbeAsync(CancellationToken cancellationToken = default);
}







