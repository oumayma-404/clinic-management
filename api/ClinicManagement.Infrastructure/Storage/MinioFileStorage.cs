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

    public async Task<string> UploadAsync(Stream file, string contentType,
        CancellationToken cancellationToken = default)
    {
        return await UploadAsync(file, contentType, null, cancellationToken);
    }

    public async Task<string> UploadAsync(Stream file, string contentType, string? customPath,
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

            // Generate storage key - use custom path if provided, otherwise generate unique key
            var storageKey = !string.IsNullOrWhiteSpace(customPath)
                ? customPath
                : $"{Guid.NewGuid()}-{DateTime.UtcNow:yyyyMMddHHmmss}";

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
}