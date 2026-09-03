using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Storage;

/// <summary>
/// The local-disk twin of <see cref="MinioResumableUploadStore"/>: each part is a file under a staging folder,
/// and completing concatenates them into the blob the row will name.
///
/// <para>⚠️ The staging folder sits <b>inside the clinic's own</b> (<c>clinics/{id}/uploads/…</c>), so an
/// abandoned upload is inside the same partition as everything else that clinic owns — and a sweep of one
/// clinic's data cannot miss it.</para>
///
/// <para>⚠️ <b>A part is written to a temporary name and moved into place.</b> A connection that dies mid-part
/// would otherwise leave a short file that looks complete, and the assembly would splice a hole into the middle
/// of a patient's radiograph — silently, since nothing downstream re-measures a part.</para>
/// </summary>
public class LocalDiskResumableUploadStore : IResumableUploadStore
{
    private readonly string _basePath;
    private readonly ILogger<LocalDiskResumableUploadStore> _logger;

    public LocalDiskResumableUploadStore(string basePath, ILogger<LocalDiskResumableUploadStore> logger)
    {
        _basePath = Path.GetFullPath(basePath);
        _logger = logger;
    }

    public Task<string> BeginAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var reference = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(StagingFolder(clinicId, reference));

        return Task.FromResult(reference);
    }

    public async Task WritePartAsync(
        Guid clinicId,
        string uploadReference,
        int partNumber,
        Stream content,
        long length,
        CancellationToken cancellationToken = default)
    {
        var folder = StagingFolder(clinicId, uploadReference);
        Directory.CreateDirectory(folder);

        var final = Path.Combine(folder, PartName(partNumber));
        var pending = final + ".partial";

        await using (var target = new FileStream(
            pending, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 64 * 1024, FileOptions.Asynchronous))
        {
            await content.CopyToAsync(target, cancellationToken);
        }

        // `File.Move` with overwrite is atomic enough for this: either the old part is still there or the new
        // one is, never a half of either. Re-sending a part therefore replaces it cleanly, which is what a
        // resumed upload does for the part it was cut off on.
        File.Move(pending, final, overwrite: true);
    }

    public async Task<string> CompleteAsync(
        Guid clinicId,
        string uploadReference,
        string contentType,
        IReadOnlyList<int> partNumbers,
        CancellationToken cancellationToken = default)
    {
        var folder = StagingFolder(clinicId, uploadReference);
        var storageKey = ClinicStorageKey.Compose(clinicId);
        var destination = Path.Combine(_basePath, storageKey.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        var pending = destination + ".partial";

        await using (var target = new FileStream(
            pending, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 64 * 1024, FileOptions.Asynchronous))
        {
            foreach (var part in partNumbers)
            {
                var path = Path.Combine(folder, PartName(part));
                if (!File.Exists(path))
                {
                    // Fail before anything is published, rather than writing a file with a hole in it.
                    target.Close();
                    File.Delete(pending);
                    throw new FileNotFoundException($"Part {part} of upload {uploadReference} is missing.");
                }

                await using var source = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

                await source.CopyToAsync(target, cancellationToken);
            }
        }

        File.Move(pending, destination, overwrite: true);
        await AbortAsync(clinicId, uploadReference, cancellationToken);

        return storageKey;
    }

    public Task AbortAsync(Guid clinicId, string uploadReference, CancellationToken cancellationToken = default)
    {
        try
        {
            var folder = StagingFolder(clinicId, uploadReference);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex)
        {
            // Never rethrown — see the interface: all three callers have already decided their own outcome.
            _logger.LogWarning(ex, "Could not release the staging area for upload {Upload}", uploadReference);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// ⚠️ The reference is a caller-supplied string, so it is checked rather than trusted: a `..` in it would
    /// name a folder outside the clinic, and deleting one recursively is the worst possible way to find out.
    /// </summary>
    private string StagingFolder(Guid clinicId, string uploadReference)
    {
        if (string.IsNullOrWhiteSpace(uploadReference)
            || uploadReference.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            throw new InvalidOperationException("An upload reference must be alphanumeric.");
        }

        var relative = Path.Combine(
            ClinicStorageKey.Prefix, clinicId.ToString(), "uploads", uploadReference);

        return Path.Combine(_basePath, relative);
    }

    /// <summary>Zero-padded so the folder listing and the assembly order agree.</summary>
    private static string PartName(int partNumber) => partNumber.ToString("D6");
}
