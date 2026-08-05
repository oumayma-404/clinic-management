namespace ClinicManagement.Application.Common.Interfaces;

public interface IFileStorage
{
    Task<string> UploadAsync(Stream file, string contentType, CancellationToken cancellationToken = default);
    Task<string> UploadAsync(Stream file, string contentType, string customPath, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken = default);
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







