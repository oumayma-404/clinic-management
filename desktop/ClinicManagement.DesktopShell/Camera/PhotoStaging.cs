using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>A photo copied off its source onto this PC, with the hash taken on the way.</summary>
public sealed record StagedPhoto(string Path, string Sha256, long Size);

/// <summary>
/// Where a photo waits between the camera and the patient's folder (<c>features/camera-photo-import</c>, phase 2):
/// <c>{root}\{itemId}\{the camera's own file name}</c>.
///
/// <para>⚠️ <b>Every photo is copied off its source first, a card included.</b> A camera on a cable has no paths, so
/// it has to be; a card could be read in place, but a dentist pulls the card as soon as the screen fills, and the
/// original is kept from this copy after the upload — moved, not copied again, so the copy is never wasted.</para>
///
/// <para>⚠️ <b>The camera's own file name, untouched, in a folder of its own.</b> The page builds a photo's capture
/// key from its name and size, so a renamed copy would not be recognised as the photo the browser path already
/// filed — and two cameras both write <c>IMG_0001.JPG</c>.</para>
///
/// <para>⚠️ The file keeps the source's date, which is the time the page falls back to when a photo has no EXIF.</para>
/// </summary>
public sealed class PhotoStaging
{
    /// <summary>Copying stops before the PC's own disk drops under this: Windows needs room to keep running.</summary>
    public const long MinFreeBytes = 2L * 1024 * 1024 * 1024;

    private const string PartSuffix = ".part";

    public PhotoStaging(string root)
    {
        Root = root;
    }

    public static string DefaultRoot =>
        System.IO.Path.Combine(ShellProfile.Local, "ClinicManagement", "camera-staging");

    public string Root { get; }

    public async Task<StagedPhoto> StageAsync(
        ICameraSource source, CameraItem item, string itemId, CancellationToken cancellationToken)
    {
        var folder = System.IO.Path.Combine(Root, itemId);
        Directory.CreateDirectory(folder);

        var target = System.IO.Path.Combine(folder, SafeName(item.Name));
        EnsureRoom(target, item.Size);

        var part = target + PartSuffix;
        string sha256;
        long size;
        try
        {
            using var hash = SHA256.Create();
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var hashing = new CryptoStream(output, hash, CryptoStreamMode.Write, leaveOpen: true))
            {
                await source.CopyToAsync(item, hashing, cancellationToken);
                await hashing.FlushFinalBlockAsync(cancellationToken);

                // Read before the streams close: the hash is the bytes that reached the disk, not a second reading.
                sha256 = Convert.ToHexString(hash.Hash!).ToLowerInvariant();
                size = output.Length;
            }

            File.Move(part, target, overwrite: true);
        }
        catch
        {
            TryDelete(part);
            throw;
        }

        if (item.TakenLocal is { } taken)
        {
            try
            {
                File.SetLastWriteTime(target, taken);
            }
            catch
            {
                // Cosmetic for a photo with EXIF, which is every photo a camera takes.
            }
        }

        return new StagedPhoto(target, sha256, size);
    }

    /// <summary>Deletes a copy and the folder holding it. Never throws.</summary>
    public void Discard(string? stagedPath)
    {
        if (string.IsNullOrEmpty(stagedPath))
        {
            return;
        }

        TryDelete(stagedPath);
        try
        {
            var folder = System.IO.Path.GetDirectoryName(stagedPath);
            if (folder != null
                && IsInside(folder)
                && Directory.Exists(folder)
                && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch
        {
            // The sweep takes it next time.
        }
    }

    /// <summary>
    /// Removes copies nobody is waiting for: a folder no live journal entry names, untouched for a day — a crash
    /// between copying and recording, or an entry the journal has since pruned. Returns how many went.
    /// </summary>
    public int Sweep(IReadOnlySet<string> liveItemIds, DateTime nowUtc)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(Root))
            {
                return 0;
            }

            foreach (var folder in Directory.EnumerateDirectories(Root))
            {
                var itemId = System.IO.Path.GetFileName(folder);
                if (liveItemIds.Contains(itemId))
                {
                    continue;
                }

                if (Directory.GetLastWriteTimeUtc(folder) > nowUtc.AddDays(-1))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(folder, recursive: true);
                    removed++;
                }
                catch
                {
                    // A file still open in a viewer: next time.
                }
            }
        }
        catch
        {
            // The folder vanished or cannot be listed: nothing to sweep.
        }

        return removed;
    }

    private bool IsInside(string folder) =>
        System.IO.Path.GetFullPath(folder).StartsWith(
            System.IO.Path.GetFullPath(Root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static void EnsureRoom(string target, long size)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(target));
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < size + MinFreeBytes)
            {
                throw new IOException(
                    $"il reste {free / (1024 * 1024)} Mo sur {root} — trop peu pour copier les photos de l'appareil.");
            }
        }
        catch (ArgumentException)
        {
            // Unknowable here; the write itself will say so.
        }
    }

    /// <summary>A camera writes plain names; only what Windows itself refuses is replaced.</summary>
    private static string SafeName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "photo.jpg" : safe;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Swept later.
        }
    }
}
