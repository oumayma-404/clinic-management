using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Copies a folder tree into a second place, adding what is missing and never deleting anything on either side —
/// the coffre's copy (<c>clinic-file-vault</c>) and the photo originals' (<c>camera-photo-import</c>) both go through
/// it, so the two cannot disagree about what « copied » means.
///
/// <para>⚠️ <b>Size is the whole freshness check.</b> Both stores are add-only by construction (the product has add
/// and delete, never replace), and hashing to decide whether to copy would mean reading every gigabyte to avoid
/// copying it.</para>
///
/// <para>⚠️ <b>The totals are of what the destination now holds of the source</b>, not of what this run happened to
/// copy. The coffre's copy used to report the bytes copied by the run as the total — so every run after the first
/// one that found nothing new said « 0 octet », the server answered « incomplete », and the 30-day alert fired over a
/// copy that was complete.</para>
///
/// <para>⚠️ <b>A copy onto the same physical disk is refused</b> (<see cref="SameDiskRefusal"/>): one disk is one
/// failure, and a second copy on it protects nothing while every screen says it does.</para>
/// </summary>
public static class AddOnlyFolderCopy
{
    /// <summary>Headroom over what is about to be written: a file arriving mid-run must not fill the volume.</summary>
    public const long FreeSpaceHeadroomBytes = 256L * 1024 * 1024;

    private const string PartSuffix = ".part";

    /// <summary>
    /// <c>Copied</c>/<c>CopiedBytes</c>: this run's work. <c>Files</c>/<c>Bytes</c>: every source file the destination
    /// now holds at the same size — what a report to the server is made of.
    /// </summary>
    public sealed record Result(int Copied, long CopiedBytes, int Files, long Bytes);

    /// <summary>
    /// Copies every file under <paramref name="source"/> that <paramref name="include"/> accepts (all of them when
    /// null) and the destination lacks. A <c>.part</c> left by an interrupted write is never a file anybody owns.
    /// </summary>
    public static async Task<Result> CopyAsync(
        string source,
        string destination,
        Func<string, bool>? include = null,
        CancellationToken cancellationToken = default)
    {
        var copied = 0;
        var copiedBytes = 0L;
        var files = 0;
        var bytes = 0L;

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(source, file);
            if (include != null && !include(relative))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);
            var info = new FileInfo(file);

            var existing = new FileInfo(target);
            if (!(existing.Exists && existing.Length == info.Length))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                EnsureFreeSpace(target, info.Length);

                // Staged then renamed, so an interrupted copy never leaves a short file that the size check above
                // would read as complete on the next run.
                var partPath = target + PartSuffix;
                try
                {
                    await using (var input = new FileStream(
                                     file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    await using (var output = File.Create(partPath))
                    {
                        await input.CopyToAsync(output, cancellationToken);
                    }

                    File.Move(partPath, target, overwrite: true);
                }
                catch
                {
                    TryDelete(partPath);
                    throw;
                }

                copied++;
                copiedBytes += info.Length;
            }

            files++;
            bytes += info.Length;
        }

        return new Result(copied, copiedBytes, files, bytes);
    }

    /// <summary>
    /// A French sentence when <paramref name="destination"/> sits on the same physical disk as
    /// <paramref name="source"/>, else null.
    ///
    /// <para>⚠️ <b>« Je ne sais pas » is not a refusal.</b> A share, a path that does not exist yet on an unplugged
    /// disk, or a volume Windows will not describe all answer null: refusing there would remove a copy that may well
    /// be protecting something, over a fact this machine cannot establish.</para>
    /// </summary>
    public static string? SameDiskRefusal(string source, string destination)
    {
        if (!PhysicalDisks.ShareADisk(source, destination))
        {
            return null;
        }

        return "Le dossier de copie est sur le même disque que les fichiers à protéger : une panne de ce disque "
               + "perdrait les deux. Choisissez un disque externe ou un autre disque du PC.";
    }

    /// <summary>Throws an <see cref="IOException"/> naming the free space when the write would fill the volume.</summary>
    public static void EnsureFreeSpace(string target, long needed)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(target));
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            var available = new DriveInfo(root).AvailableFreeSpace;
            if (available < needed + FreeSpaceHeadroomBytes)
            {
                throw new IOException(
                    $"espace insuffisant sur {root} ({available / (1024 * 1024)} Mo disponibles).");
            }
        }
        catch (ArgumentException)
        {
            // A UNC path has no DriveInfo. Let the write itself be the check.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: unknowable here, and the write will say so.
        }
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
            // Best-effort: a leftover .part is skipped by every reader and replaced by the next run.
        }
    }
}
