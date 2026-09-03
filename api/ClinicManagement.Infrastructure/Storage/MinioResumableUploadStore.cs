using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.Exceptions;

namespace ClinicManagement.Infrastructure.Storage;

/// <summary>
/// Parts of an in-flight upload, as ordinary objects under a staging prefix inside the owning clinic.
///
/// <para>⚠️ <b>Not S3 multipart, and that was checked rather than assumed.</b> <c>Minio 5.0.0</c> keeps
/// <c>NewMultipartUploadAsync</c>, <c>PutObjectPartAsync</c> and <c>CompleteMultipartUploadAsync</c> internal —
/// its public surface is only <c>ListIncompleteUploads</c> and <c>RemoveIncompleteUploadAsync</c> — and it has
/// no <c>ComposeObject</c>. Bumping the package is a dependency change on a working object store for an API
/// this can do without.</para>
///
/// <para>⚠️ <b><see cref="CompleteAsync"/> streams; it never buffers.</b> The parts are handed to
/// <c>PutObjectAsync</c> as one <see cref="ConcatenatedStream"/> with the total declared up front, so a
/// gigabyte costs the server one part-sized buffer rather than a gigabyte. It deliberately does <b>not</b> go
/// through <see cref="IFileStorage.UploadAsync"/>: that method buffers a non-seekable stream whole to learn its
/// size, which is exactly what this exists to avoid.</para>
///
/// <para>⚠️ The staging prefix lives <b>inside the clinic</b> (<c>clinics/{id}/uploads/…</c>) rather than in a
/// bucket-wide scratch area, so a half-finished upload is covered by the same tenant partitioning as everything
/// else — including the archive's own prefix walk.</para>
/// </summary>
public class MinioResumableUploadStore : IResumableUploadStore
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly ILogger<MinioResumableUploadStore> _logger;

    public MinioResumableUploadStore(
        IMinioClient minioClient,
        string bucketName,
        ILogger<MinioResumableUploadStore> logger)
    {
        _minioClient = minioClient;
        _bucketName = bucketName;
        _logger = logger;
    }

    public Task<string> BeginAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        // Nothing to create: an object store has no directories, so the prefix exists the moment a part is
        // written under it. The reference is minted here so the row can record it before anything arrives.
        return Task.FromResult(Guid.NewGuid().ToString("N"));
    }

    public async Task WritePartAsync(
        Guid clinicId,
        string uploadReference,
        int partNumber,
        Stream content,
        long length,
        CancellationToken cancellationToken = default)
    {
        await EnsureBucketAsync(cancellationToken);

        // Overwriting by key is what makes a re-sent part idempotent — which a resumed upload always does for
        // the part it was cut off on.
        await _minioClient.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(PartKey(clinicId, uploadReference, partNumber))
                .WithStreamData(content)
                .WithObjectSize(length)
                .WithContentType("application/octet-stream"),
            cancellationToken);
    }

    public async Task<string> CompleteAsync(
        Guid clinicId,
        string uploadReference,
        string contentType,
        IReadOnlyList<int> partNumbers,
        CancellationToken cancellationToken = default)
    {
        await EnsureBucketAsync(cancellationToken);

        var total = 0L;
        var openers = new List<Func<CancellationToken, Task<Stream>>>(partNumbers.Count);

        foreach (var part in partNumbers)
        {
            var key = PartKey(clinicId, uploadReference, part);

            // The size has to be known before the upload starts, so each part is stat'ed first — which also
            // fails here, cleanly, if a part went missing rather than half-way through writing the final blob.
            var stat = await _minioClient.StatObjectAsync(
                new StatObjectArgs().WithBucket(_bucketName).WithObject(key), cancellationToken);

            total += stat.Size;
            openers.Add(ct => OpenPartAsync(key, ct));
        }

        var storageKey = ClinicStorageKey.Compose(clinicId);

        await using (var assembled = new ConcatenatedStream(openers, total))
        {
            await _minioClient.PutObjectAsync(
                new PutObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(storageKey)
                    .WithStreamData(assembled)
                    .WithObjectSize(total)
                    .WithContentType(contentType),
                cancellationToken);
        }

        // ⚠️ After the final object exists, never before: a sweep that removed the parts first would leave an
        // upload that failed at the last step with nothing to retry from.
        await AbortAsync(clinicId, uploadReference, cancellationToken);

        _logger.LogInformation(
            "Assembled {Parts} parts into {StorageKey} ({Bytes} bytes)", partNumbers.Count, storageKey, total);

        return storageKey;
    }

    public async Task AbortAsync(
        Guid clinicId, string uploadReference, CancellationToken cancellationToken = default)
    {
        try
        {
            var keys = await ListStagedKeysAsync(
                StagingPrefix(clinicId, uploadReference), cancellationToken);

            foreach (var key in keys)
            {
                await _minioClient.RemoveObjectAsync(
                    new RemoveObjectArgs().WithBucket(_bucketName).WithObject(key), cancellationToken);
            }
        }
        catch (BucketNotFoundException)
        {
            // Nothing can be staged in a bucket that is not there.
        }
        catch (Exception ex)
        {
            // ⚠️ Never rethrown. This runs from the abandon endpoint, from the expiry sweep and from a failed
            // completion, and in all three the caller's own outcome is already decided — an unreclaimed part is
            // wasted bytes, not a failure to report to anybody.
            _logger.LogWarning(ex, "Could not release the staging area for upload {Upload}", uploadReference);
        }
    }

    /// <summary>
    /// Every object under a prefix.
    ///
    /// <para>⚠️ <c>ListObjectsAsync</c> returns an <c>IObservable&lt;Item&gt;</c>, not an async sequence, and this
    /// solution takes no reactive dependency — so the subscription is bridged by hand rather than by pulling in
    /// <c>System.Reactive</c> for one call. The completion callback and the error callback both settle the same
    /// task, so a listing that fails is awaited into an exception instead of returning a short list, which here
    /// would read as « nothing left to clean up ».</para>
    /// </summary>
    private async Task<IReadOnlyList<string>> ListStagedKeysAsync(
        string prefix, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = _minioClient
            .ListObjectsAsync(
                new ListObjectsArgs().WithBucket(_bucketName).WithPrefix(prefix).WithRecursive(true),
                cancellationToken)
            .Subscribe(
                item => keys.Add(item.Key),
                error => completion.TrySetException(error),
                () => completion.TrySetResult(true));

        await using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
        {
            await completion.Task;
        }

        return keys;
    }

    private async Task<Stream> OpenPartAsync(string key, CancellationToken cancellationToken)
    {
        // Buffered, unlike `MinioFileStorage.DownloadAsync`'s pipe, and the difference is deliberate: a part is
        // bounded by the chunk size the server itself chose, so this is a few megabytes at a time and the
        // simpler read has no background task whose failure has to be threaded back through a writer.
        var buffer = new MemoryStream();

        await _minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(key)
                .WithCallbackStream(async (stream, ct) => { await stream.CopyToAsync(buffer, ct); }),
            cancellationToken);

        buffer.Position = 0;
        return buffer;
    }

    private static string StagingPrefix(Guid clinicId, string uploadReference) =>
        $"{ClinicStorageKey.Prefix}/{clinicId}/uploads/{uploadReference}/";

    /// <summary>Zero-padded so a lexical listing of the staging prefix is also the assembly order.</summary>
    private static string PartKey(Guid clinicId, string uploadReference, int partNumber) =>
        $"{StagingPrefix(clinicId, uploadReference)}{partNumber:D6}";

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        var exists = await _minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucketName), cancellationToken);

        if (!exists)
        {
            await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName), cancellationToken);
        }
    }
}
