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
    /// e-invoice outbox runs <c>SystemWide</c> (no clinic in scope at all) and would silently write an
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
    /// </summary>
    Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Removes the blob stored under <paramref name="storageKey"/>, verbatim — see <see cref="DownloadAsync"/>.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

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







