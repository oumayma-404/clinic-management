using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.Exceptions;

namespace ClinicManagement.Infrastructure.Storage;

public class MinioFileStorage : IFileStorage
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly ILogger<MinioFileStorage> _logger;

    public MinioFileStorage(
        IMinioClient minioClient,
        string bucketName,
        ILogger<MinioFileStorage> logger)
    {
        _minioClient = minioClient;
        _bucketName = bucketName;
        _logger = logger;
    }

    public async Task<string> UploadAsync(Stream file, string contentType, Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await UploadAsync(file, contentType, clinicId, null, cancellationToken);
    }

    public async Task<string> UploadAsync(Stream file, string contentType, Guid clinicId, string? relativePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure bucket exists
            var bucketExists = await _minioClient.BucketExistsAsync(
                new BucketExistsArgs()
                    .WithBucket(_bucketName),
                cancellationToken);

            if (!bucketExists)
            {
                await _minioClient.MakeBucketAsync(
                    new MakeBucketArgs()
                        .WithBucket(_bucketName),
                    cancellationToken);
                _logger.LogInformation("Created bucket: {BucketName}", _bucketName);
            }

            // US-5: one composer for both backends — clinics/{clinicId}/ then the caller's path or a unique leaf.
            var storageKey = ClinicStorageKey.Compose(clinicId, relativePath);

            // Handle stream - MinIO requires knowing the size, so we may need to buffer non-seekable streams
            long streamLength;
            MemoryStream? memoryStream = null;
            Stream streamToUpload;

            if (file.CanSeek)
            {
                streamLength = file.Length - file.Position;
                streamToUpload = file;
            }
            else
            {
                // If stream is not seekable, we need to read it into memory to get the size
                memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream, cancellationToken);
                memoryStream.Position = 0;
                streamLength = memoryStream.Length;
                streamToUpload = memoryStream;
            }

            try
            {
                // Upload file
                await _minioClient.PutObjectAsync(
                    new PutObjectArgs()
                        .WithBucket(_bucketName)
                        .WithObject(storageKey)
                        .WithStreamData(streamToUpload)
                        .WithObjectSize(streamLength)
                        .WithContentType(contentType),
                    cancellationToken);

                _logger.LogInformation(
                    "File uploaded successfully. Storage key: {StorageKey}, ContentType: {ContentType}",
                    storageKey, contentType);
            }
            finally
            {
                // Dispose memory stream if we created one
                memoryStream?.Dispose();
            }

            return storageKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to MinIO");
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var memoryStream = new MemoryStream();

            // Use the async callback overload (Func<Stream, CancellationToken, Task>) so MinIO awaits the
            // copy before it closes the underlying HTTP response stream. The synchronous Action<Stream>
            // overload turns an `async` lambda into async-void: the copy continues after MinIO has already
            // disposed the stream, throwing on a background thread (NRE) and taking down the whole host.
            await _minioClient.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(storageKey)
                    .WithCallbackStream(async (stream, ct) => { await stream.CopyToAsync(memoryStream, ct); }),
                cancellationToken);

            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from MinIO. Storage key: {StorageKey}", storageKey);
            throw;
        }
    }

    /// <summary>
    /// Writes bytes back at a key a row already holds. MinIO object names are flat strings, so a key restored
    /// verbatim names exactly the object the row points at — including a pre-US-5 flat one (EC-4).
    /// </summary>
    public async Task RestoreAtKeyAsync(
        Stream file, string contentType, string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureBucketAsync(cancellationToken);

            // PutObjectArgs needs the length up front, and an archive entry's stream is not seekable.
            MemoryStream? buffered = null;
            Stream source;
            long length;

            if (file.CanSeek)
            {
                length = file.Length - file.Position;
                source = file;
            }
            else
            {
                buffered = new MemoryStream();
                await file.CopyToAsync(buffered, cancellationToken);
                buffered.Position = 0;
                length = buffered.Length;
                source = buffered;
            }

            try
            {
                await _minioClient.PutObjectAsync(
                    new PutObjectArgs()
                        .WithBucket(_bucketName)
                        .WithObject(storageKey)
                        .WithStreamData(source)
                        .WithObjectSize(length)
                        .WithContentType(contentType),
                    cancellationToken);

                _logger.LogInformation(
                    "Blob restored at its original key. Storage key: {StorageKey}, ContentType: {ContentType}",
                    storageKey, contentType);
            }
            finally
            {
                buffered?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring blob to MinIO. Storage key: {StorageKey}", storageKey);
            throw;
        }
    }

    /// <summary>
    /// Whether an object already exists, through <c>StatObject</c>. A missing object raises rather than returning
    /// null, and <b>that particular refusal</b> is the answer.
    ///
    /// <para>⚠️ <b>Only a genuine not-found reads as « absent ».</b> A catch-all read every failure that way — a
    /// network blip, an expired credential, a bucket-policy refusal, a throttle, a 5xx — and this single boolean
    /// is the whole of the archive restore's « existing bytes are left alone », with <c>RestoreAtKeyAsync</c>
    /// overwriting unconditionally behind it. So during any MinIO instability a restore silently replaced a
    /// radiograph or a scanned consent the practice had put there <i>after</i> the archive was taken, and counted
    /// it as a success: the exact rollback of recent work the additive design exists to prevent. Everything else
    /// propagates and is recorded as a French warning against the file it belongs to.</para>
    /// </summary>
    public async Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _minioClient.StatObjectAsync(
                new StatObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(storageKey),
                cancellationToken);

            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
        catch (BucketNotFoundException)
        {
            // Nothing can be there if the bucket is not: UploadAsync creates it on demand, so this is « absent »
            // rather than « unreachable » — the same reading ProbeAsync gives a missing bucket.
            return false;
        }
    }

    /// <summary>Creates the bucket on demand, exactly as <see cref="UploadAsync(Stream, string, Guid, string?, CancellationToken)"/> does.</summary>
    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        var bucketExists = await _minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucketName), cancellationToken);

        if (!bucketExists)
        {
            await _minioClient.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_bucketName), cancellationToken);
            _logger.LogInformation("Created bucket: {BucketName}", _bucketName);
        }
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _minioClient.RemoveObjectAsync(
                new RemoveObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(storageKey),
                cancellationToken);

            _logger.LogInformation("File deleted successfully. Storage key: {StorageKey}", storageKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from MinIO. Storage key: {StorageKey}", storageKey);
            throw;
        }
    }

    /// <summary>
    /// Asks MinIO whether the bucket exists. That single call exercises everything the storage path depends on —
    /// DNS, the endpoint, TLS and the credentials — and it neither creates nor stores anything.
    ///
    /// <para>⚠️ A <b>missing</b> bucket <b>returns normally</b> (review finding 19). The distinction this docstring
    /// used to promise — reachable-but-unusable vs. unreachable — had no channel to travel on: throwing was the only
    /// signal available, and <c>FileStorageHealthCheck</c> catches every exception into one <c>Degraded</c> plus
    /// « the file storage is unreachable ». So the promise never reached an operator, and it made a correctly deployed
    /// brand-new stack answer <c>storage: Degraded</c> from first boot with an Error line every probe tick, because
    /// neither compose file creates the bucket and <c>UploadAsync</c> creates it on demand — i.e. the first signal an
    /// operator checks read as a fault on a healthy deployment. What this call verifies is what it can actually
    /// distinguish: DNS, the endpoint, TLS and the credentials. A bucket that does not exist yet is logged, not
    /// graded.</para>
    /// </summary>
    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        var exists = await _minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucketName),
            cancellationToken);

        if (!exists)
        {
            _logger.LogInformation(
                "MinIO is reachable; the bucket {BucketName} does not exist yet and will be created on first upload.",
                _bucketName);
        }
    }
}