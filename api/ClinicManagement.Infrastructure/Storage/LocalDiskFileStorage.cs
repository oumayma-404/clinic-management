using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Storage;

/// <summary>
/// Local-disk implementation of <see cref="IFileStorage"/> used in Local (offline) mode.
/// Stores blobs under a configurable base folder and returns an opaque relative storage key.
/// Mirrors <see cref="MinioFileStorage"/> semantics (unique guid keys, deterministic custom-path
/// overwrite, seekable download stream, idempotent delete) so the file-consuming handlers behave
/// identically in both modes.
/// </summary>
public class LocalDiskFileStorage : IFileStorage
{
    private readonly string _basePath;
    private readonly ILogger<LocalDiskFileStorage> _logger;

    public LocalDiskFileStorage(string basePath, ILogger<LocalDiskFileStorage> logger)
    {
        _basePath = Path.GetFullPath(basePath);
        _logger = logger;
    }

    public Task<string> UploadAsync(Stream file, string contentType,
        CancellationToken cancellationToken = default)
    {
        return UploadAsync(file, contentType, null, cancellationToken);
    }

    public async Task<string> UploadAsync(Stream file, string contentType, string? customPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Storage key: a deterministic custom path (e.g. the clinic-logo path) or a unique
            // guid-based key, matching MinioFileStorage's key format.
            var storageKey = !string.IsNullOrWhiteSpace(customPath)
                ? customPath
                : $"{Guid.NewGuid()}-{DateTime.UtcNow:yyyyMMddHHmmss}";

            var fullPath = ResolveWithinBase(storageKey);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // FileMode.Create overwrites in place (mirrors MinIO custom-path overwrite semantics).
            await using (var destination = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await file.CopyToAsync(destination, cancellationToken);
            }

            _logger.LogInformation(
                "File stored locally. Storage key: {StorageKey}, ContentType: {ContentType}",
                storageKey, contentType);

            return storageKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing file to local disk");
            throw;
        }
    }

    public async Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = ResolveWithinBase(storageKey);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File not found for storage key: {storageKey}");
            }

            // Buffer into a seekable MemoryStream (mirrors MinioFileStorage; releases the file handle).
            var memoryStream = new MemoryStream();
            await using (var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await source.CopyToAsync(memoryStream, cancellationToken);
            }

            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file from local disk. Storage key: {StorageKey}", storageKey);
            throw;
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = ResolveWithinBase(storageKey);

            // Delete is idempotent (mirrors MinIO/S3): a missing key is not an error.
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("File deleted from local disk. Storage key: {StorageKey}", storageKey);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from local disk. Storage key: {StorageKey}", storageKey);
            throw;
        }
    }

    /// <summary>
    /// Confirms the base folder exists and is writable, by creating it if absent and then opening — and
    /// immediately deleting — a probe file. The write half is the point: an unmounted volume, a full disk and a
    /// folder the service account cannot write to all present as an existing directory, and every one of them
    /// breaks the first upload rather than the check.
    ///
    /// <para>The probe file is named per attempt so two concurrent checks cannot collide on it, and it is deleted
    /// in a <c>finally</c> so a failure mid-way leaves nothing behind.</para>
    /// </summary>
    public async Task ProbeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_basePath);

        var probePath = Path.Combine(_basePath, $".health-{Guid.NewGuid():N}");

        try
        {
            await using var probe = new FileStream(
                probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await probe.WriteAsync(new byte[] { 0 }, cancellationToken);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    /// <summary>
    /// Resolves a storage key to an absolute path and guarantees it stays within the base folder,
    /// so a crafted key (e.g. one containing "..") can never escape the storage root.
    /// </summary>
    private string ResolveWithinBase(string storageKey)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, storageKey));

        var baseWithSeparator = _basePath.EndsWith(Path.DirectorySeparatorChar)
            ? _basePath
            : _basePath + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(baseWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid storage key resolves outside the storage root: {storageKey}");
        }

        return fullPath;
    }
}
