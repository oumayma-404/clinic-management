using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// A camera on a USB cable (<c>features/camera-photo-import</c>, phase 2) — read through Windows' portable-device
/// layer (<c>Windows.Devices.Portable.StorageDevice</c>), the same one Explorer shows « Canon EOS 250D » with.
///
/// <para>⚠️ <b>Not verified on a real camera yet</b>: the pilot's Canon and Nikon are the test. Every call is
/// guarded, so a camera Windows will not describe is simply « not found », and the card in a reader — which every
/// camera supports — is the path that does not depend on it.</para>
///
/// <para>⚠️ <b>No paths.</b> A file on a camera has none, which is why every photo is copied off it before the page
/// can be handed a file (<see cref="PhotoStaging"/>), and why the camera's own clock cannot be read here — the
/// matcher's offset detection stands in for it.</para>
/// </summary>
public sealed class WpdCameraSource : ICameraSource
{
    private readonly IReadOnlyList<(string Prefix, StorageFolder Folder)> _dcims;
    private readonly Dictionary<string, StorageFile> _files = new(StringComparer.OrdinalIgnoreCase);

    public WpdCameraSource(string sourceKey, string label, IReadOnlyList<(string Prefix, StorageFolder Folder)> dcims)
    {
        SourceKey = sourceKey;
        Label = label;
        _dcims = dcims;
    }

    public string SourceKey { get; }

    public string Label { get; }

    public async Task<IReadOnlyList<CameraItem>> ListAsync(CancellationToken cancellationToken)
    {
        _files.Clear();
        var found = new List<(string ObjectId, StorageFile File)>();
        foreach (var (prefix, dcim) in _dcims)
        {
            await WalkAsync(dcim, prefix.Length == 0 ? "DCIM" : prefix + "\\DCIM", found, cancellationToken);
        }

        // ⚠️ Chosen on the date the folder listing already carries, BEFORE the per-photo property read: on a cable that
        // read is a round trip per file, and a cap applied in walk order dropped the newest folder.
        var newest = CameraListing.KeepNewest(
            found, f => DateOf(f.File.DateCreated), f => f.ObjectId, CardCameraSource.MaxFiles);

        var items = new List<CameraItem>();
        foreach (var (objectId, file) in newest)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var properties = await file.GetBasicPropertiesAsync();

            // A device that reports no date answers year 1601; its creation date is the next best fact.
            items.Add(new CameraItem(
                objectId,
                file.Name,
                (long)properties.Size,
                DateOf(properties.DateModified) ?? DateOf(file.DateCreated),
                CameraFileKinds.Of(file.Name)));
            _files[objectId] = file;
        }

        return items;
    }

    public async Task CopyToAsync(CameraItem item, Stream destination, CancellationToken cancellationToken)
    {
        if (!_files.TryGetValue(item.ObjectId, out var file))
        {
            throw new IOException("La photo n'est plus sur l'appareil.");
        }

        await using var input = await file.OpenStreamForReadAsync();
        await input.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>Names only — no per-file read. Newest DCF folder first, so the read limit leaves the oldest.</summary>
    private static async Task WalkAsync(
        StorageFolder folder,
        string relative,
        List<(string ObjectId, StorageFile File)> found,
        CancellationToken cancellationToken)
    {
        if (found.Count >= CardCameraSource.MaxFiles * CardCameraSource.ReadAhead)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var file in await folder.GetFilesAsync())
        {
            if (!CameraFileKinds.IsIgnoredName(file.Name))
            {
                found.Add((relative + "\\" + file.Name, file));
            }
        }

        foreach (var sub in (await folder.GetFoldersAsync()).OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!CameraFileKinds.IsIgnoredFolder(sub.Name))
            {
                await WalkAsync(sub, relative + "\\" + sub.Name, found, cancellationToken);
            }
        }
    }

    private static DateTime? DateOf(DateTimeOffset date) =>
        date.Year > 1980 ? DateTime.SpecifyKind(date.LocalDateTime, DateTimeKind.Unspecified) : null;
}
