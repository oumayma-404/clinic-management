using System;
using System.Collections.Generic;
using System.IO;
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

    public CardCameraSource(string root, string sourceKey, string label)
    {
        Root = root;
        SourceKey = sourceKey;
        Label = label;
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

        var pending = new Stack<string>();
        pending.Push(dcim);

        while (pending.Count > 0 && items.Count < MaxFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folder = pending.Pop();
            string[] files;
            string[] folders;
            try
            {
                files = Directory.GetFiles(folder);
                folders = Directory.GetDirectories(folder);
            }
            catch
            {
                // One unreadable folder (a card the camera half-wrote) does not hide the rest.
                continue;
            }

            foreach (var sub in folders)
            {
                if (!CameraFileKinds.IsIgnoredFolder(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (CameraFileKinds.IsIgnoredName(name))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(file);
                    items.Add(new CameraItem(
                        Path.GetRelativePath(Root, file),
                        name,
                        info.Length,
                        DateTime.SpecifyKind(info.LastWriteTime, DateTimeKind.Unspecified),
                        CameraFileKinds.Of(name)));
                }
                catch
                {
                    continue;
                }

                if (items.Count >= MaxFiles)
                {
                    break;
                }
            }
        }

        return items;
    }
}
