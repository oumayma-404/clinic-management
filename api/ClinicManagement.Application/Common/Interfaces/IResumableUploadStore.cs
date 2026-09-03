namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Where the pieces of an upload live while it is still arriving.
///
/// <para>⚠️ <b>Its own seam rather than more overloads on <see cref="IFileStorage"/>, deliberately.</b> That
/// interface's contract is « a blob, whole, at a key », and <c>ClinicStorageKeyTests</c> reflects over it to
/// prove no upload can name a key without a clinic. A half-written file is not a blob: it has no key anyone may
/// read, it must be reclaimable when abandoned, and it exists only between two requests.</para>
///
/// <para>⚠️ <b>Not S3 multipart, and that was measured rather than assumed.</b> The obvious implementation is the
/// object store's own multipart API — but <c>Minio 5.0.0</c> keeps <c>NewMultipartUploadAsync</c>,
/// <c>PutObjectPartAsync</c> and <c>CompleteMultipartUploadAsync</c> internal, exposing only
/// <c>ListIncompleteUploads</c> and <c>RemoveIncompleteUploadAsync</c>, and it has no <c>ComposeObject</c>. So a
/// part is an ordinary object under a staging prefix and {@see CompleteAsync} concatenates them — <b>streamed</b>,
/// never buffered, so a gigabyte costs the server no memory. The bytes travel between the API and the object
/// store, which on every deployment are the same host or the same private network; they do not touch the
/// clinic's uplink a second time.</para>
///
/// <para>⚠️ <b>A part is not validated and its bytes are not trusted until <see cref="CompleteAsync"/>.</b> The
/// judgement — extension, signature, size — happens in the handlers, against the first part's header and the
/// assembled length. Nothing here decides what may be stored.</para>
/// </summary>
public interface IResumableUploadStore
{
    /// <summary>
    /// Opens a staging area for <paramref name="clinicId"/> and returns the reference the other three methods
    /// take. The reference is opaque and is persisted on the upload's row; it names nothing a client may read.
    /// </summary>
    Task<string> BeginAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores one part. ⚠️ <b>Idempotent by part number</b> — a client that lost the response and re-sent the
    /// same part must not produce two, and a resumed upload legitimately re-sends the part it was cut off on.
    /// </summary>
    Task WritePartAsync(
        Guid clinicId,
        string uploadReference,
        int partNumber,
        Stream content,
        long length,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Concatenates the named parts, in the order given, into one stored blob and returns its key. The staging
    /// area is released on success.
    ///
    /// <para>⚠️ The caller passes the part numbers rather than the store discovering them: the upload's row is
    /// the record of what arrived, and a store that listed its own staging area could assemble a part the row
    /// never acknowledged.</para>
    /// </summary>
    Task<string> CompleteAsync(
        Guid clinicId,
        string uploadReference,
        string contentType,
        IReadOnlyList<int> partNumbers,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the staging area, keeping nothing. ⚠️ <b>Idempotent and never throws for an absent one</b>: it
    /// is called from the abandon endpoint, from the expiry sweep, and from a failed completion, so any of the
    /// three may find the work already undone.
    /// </summary>
    Task AbortAsync(Guid clinicId, string uploadReference, CancellationToken cancellationToken = default);
}
