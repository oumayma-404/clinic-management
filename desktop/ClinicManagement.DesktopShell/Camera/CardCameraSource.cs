using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// A camera's card in a reader — or the camera itself when it mounts as a drive: whatever holds a <c>DCIM</c> folder
/// at its root, the folder every camera writes to (DCF).
/// </summary>
public sealed class CardCameraSource : ICameraSource
{
    /// <summary>At most this many files are listed — a drive mounted at the wrong folder can hold a whole disk.</summary>
    public const int MaxFiles = 5000;

    /// <summary>How many names are read to choose the newest <see cref="MaxFiles"/> before the walk gives up.</summary>
    public const int ReadAhead = 4;

    private readonly int _maxFiles;

    public CardCameraSource(string root, string sourceKey, string label, int maxFiles = MaxFiles)
    {
        Root = root;
        SourceKey = sourceKey;
        Label = label;
        _maxFiles = maxFiles;
    }

    public string Root { get; }

    public string SourceKey { get; }

    public string Label { get; }

    public Task<IReadOnlyList<CameraItem>> ListAsync(CancellationToken cancellationToken) =>
        Task.Run(() => List(cancellationToken), cancellationToken);

    public async Task CopyToAsync(CameraItem item, Stream destination, CancellationToken cancellationToken)
    {
        // The id came from this source's own listing; a path leaving the card is refused all the same.
        var root = Path.GetFullPath(Root).TrimEnd('\\') + "\\";
        var path = Path.GetFullPath(Path.Combine(Root, item.ObjectId));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Chemin hors de la carte.");
        }

        await using var input = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        await input.CopyToAsync(destination, cancellationToken);
    }

    private IReadOnlyList<CameraItem> List(CancellationToken cancellationToken)
    {
        var items = new List<CameraItem>();
        var dcim = Path.Combine(Root, "DCIM");
        if (!Directory.Exists(dcim))
        {
            return items;
        }

        // ⚠️ Read past the cap, then keep the newest (CameraListing): a card lists its photos in the order they were
        // written. The read still ends somewhere, newest folder first, so what it leaves unread is the oldest.
        var readLimit = _maxFiles * ReadAhead;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(dcim));

        while (pending.Count > 0 && items.Count < readLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folder = pending.Pop();
            FileInfo[] files;
            DirectoryInfo[] folders;
            try
            {
                // The listing carries each file's size and date: no second read per photo.
                files = folder.GetFiles();
                folders = folder.GetDirectories();
            }
            catch
            {
                // One unreadable folder (a card the camera half-wrote) does not hide the rest.
                continue;
            }

            // Pushed in ascending order, so « 102CANON » comes off the stack before « 100CANON ».
            foreach (var sub in folders.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!CameraFileKinds.IsIgnoredFolder(sub.Name))
                {
                    pending.Push(sub);
                }
            }

            foreach (var info in files)
            {
                if (CameraFileKinds.IsIgnoredName(info.Name))
                {
                    continue;
                }

                try
                {
                    items.Add(new CameraItem(
                        Path.GetRelativePath(Root, info.FullName),
                        info.Name,
                        info.Length,
                        DateTime.SpecifyKind(info.LastWriteTime, DateTimeKind.Unspecified),
                        CameraFileKinds.Of(info.Name)));
                }
                catch
                {
                    continue;
                }

                if (items.Count >= readLimit)
                {
                    break;
                }
            }
        }

        return CameraListing.KeepNewest(items, item => item.TakenLocal, item => item.ObjectId, _maxFiles);
    }
}
