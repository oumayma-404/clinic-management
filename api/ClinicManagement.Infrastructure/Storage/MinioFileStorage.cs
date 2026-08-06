using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Minio;

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