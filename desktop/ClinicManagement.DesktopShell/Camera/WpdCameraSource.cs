using System;
using System.Collections.Generic;
using System.IO;
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
        var items = new List<CameraItem>();
        foreach (var (prefix, dcim) in _dcims)
        {
            await WalkAsync(dcim, prefix.Length == 0 ? "DCIM" : prefix + "\\DCIM", items, cancellationToken);
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

    private async Task WalkAsync(
        StorageFolder folder, string relative, List<CameraItem> items, CancellationToken cancellationToken)
    {
        if (items.Count >= CardCameraSource.MaxFiles)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var file in await folder.GetFilesAsync())
        {
            if (CameraFileKinds.IsIgnoredName(file.Name))
            {
                continue;
            }

            var properties = await file.GetBasicPropertiesAsync();
            var objectId = relative + "\\" + file.Name;

            // A device that reports no date answers year 1601; its creation date is the next best fact.
            var taken = properties.DateModified.Year > 1980 ? properties.DateModified : file.DateCreated;
            items.Add(new CameraItem(
                objectId,
                file.Name,
                (long)properties.Size,
                taken.Year > 1980 ? DateTime.SpecifyKind(taken.LocalDateTime, DateTimeKind.Unspecified) : null,
                CameraFileKinds.Of(file.Name)));
            _files[objectId] = file;

            if (items.Count >= CardCameraSource.MaxFiles)
            {
                return;
            }
        }

        foreach (var sub in await folder.GetFoldersAsync())
        {
            if (!CameraFileKinds.IsIgnoredFolder(sub.Name))
            {
                await WalkAsync(sub, relative + "\\" + sub.Name, items, cancellationToken);
            }
        }
    }
}
