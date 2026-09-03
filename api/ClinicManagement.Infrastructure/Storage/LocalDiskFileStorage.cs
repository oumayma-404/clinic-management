using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Storage;

/// <summary>
/// Local-disk implementation of <see cref="IFileStorage"/> used in Local (offline) mode.
/// Stores blobs under a configurable base folder and returns an opaque relative storage key.
/// Mirrors <see cref="MinioFileStorage"/> semantics (unique guid keys, deterministic custom-path
/// overwrite, streamed download, idempotent delete) so the file-consuming handlers behave
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

    public Task<string> UploadAsync(Stream file, string contentType, Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return UploadAsync(file, contentType, clinicId, null, cancellationToken);
    }

    public async Task<string> UploadAsync(Stream file, string contentType, Guid clinicId, string? relativePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // US-5: the same composer MinioFileStorage uses, so a key means the same thing in both backends.
            var storageKey = ClinicStorageKey.Compose(clinicId, relativePath);

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

    public Task<Stream> DownloadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = ResolveWithinBase(storageKey);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File not found for storage key: {storageKey}");
            }

            // ⚠️ The handle itself, not a copy of the bytes. This used to buffer the whole file into a
            // MemoryStream « to release the file handle », which on a clinic PC serving its own LAN meant a
            // 150 Mo study lived twice — once on disk and once in the server's memory, per concurrent reader.
            // The caller disposes the stream; `Asynchronous` and `SequentialScan` are what make that cheap.
            Stream file = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Task.FromResult(file);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file from local disk. Storage key: {StorageKey}", storageKey);
            throw;
        }
    }

    /// <summary>
    /// Writes bytes back at a key a row already holds. <see cref="ResolveWithinBase"/> still applies, so a
    /// crafted key inside an archive cannot write outside the storage root — the archive is a file a practice
    /// keeps on a laptop, so it is untrusted input by the time it comes back.
    /// </summary>
    public async Task RestoreAtKeyAsync(
        Stream file, string contentType, string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = ResolveWithinBase(storageKey);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var destination = new FileStream(
                fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(destination, cancellationToken);

            _logger.LogInformation(
                "Blob restored to local disk at its original key. Storage key: {StorageKey}, ContentType: {ContentType}",
                storageKey, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring blob to local disk. Storage key: {StorageKey}", storageKey);
            throw;
        }
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(ResolveWithinBase(storageKey)));
    }

    public Task<long?> GetLengthAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(ResolveWithinBase(storageKey));

        return Task.FromResult(info.Exists ? info.Length : (long?)null);
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
