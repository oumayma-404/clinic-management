using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>The full-size photo this PC now keeps, as the server is told about it.</summary>
public sealed record KeptOriginal(Guid FileId, string Sha256, long SizeBytes);

/// <summary>How keeping one original went: kept, or a French sentence saying why not.</summary>
public sealed record KeepOutcome(KeptOriginal? Original, string? Refusal);

/// <summary>
/// The full-size originals of camera photos, on the clinic PC (<c>features/camera-photo-import</c>, storage A): the
/// cloud stores a 2 560 px copy that opens anywhere, this folder keeps the photo exactly as the camera took it.
///
/// <para>⚠️ <b><c>%ProgramData%</c>, beside the coffre and with its ACL</b>: machine-wide, so the dentist and reception
/// see one store, and locked to the account that created it plus administrators. Another account cannot keep
/// originals here and is told so — the cloud copy still files, which is the rule of the whole design: losing this PC
/// loses detail, never a photo.</para>
///
/// <para>⚠️ <b>Never below <see cref="MinFreeBytes"/> free.</b> At thirty photos a day this folder grows by ~60 Go a
/// year; a PC whose disk fills stops booting Windows updates, then stops starting the app. Below the floor an original
/// is simply not kept (the screen says so), and the cloud copy is filed regardless.</para>
/// </summary>
public sealed class OriginalsStore
{
    public const long MinFreeBytes = 10L * 1024 * 1024 * 1024;

    private readonly object _indexGate = new();

    /// <summary>Index lines not written yet — the sheet was open elsewhere. Written before the next one.</summary>
    private readonly List<string> _unwrittenLines = new();

    public OriginalsStore(string root)
    {
        Root = root;
    }

    public static string DefaultRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ClinicManagement",
            "photos");

    public string Root { get; }

    public string PathFor(Guid fileId) => Path.Combine(Root, OriginalsPathPlanner.RelativePathFor(fileId));

    public bool Holds(Guid fileId)
    {
        try
        {
            return File.Exists(PathFor(fileId));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Moves a staged photo into the store under its file id and writes its line in the index. The staged copy is
    /// gone afterwards whatever happens — kept, or discarded by the caller on a refusal.
    /// </summary>
    public KeepOutcome Keep(
        string stagedPath, string sha256, Guid fileId, string? patientName, DateTime? takenLocal, string originalName)
    {
        long size;
        try
        {
            var target = PathFor(fileId);
            var staged = new FileInfo(stagedPath);
            if (!staged.Exists)
            {
                return new KeepOutcome(null, "La copie de la photo a disparu de ce PC avant d'être gardée.");
            }

            size = staged.Length;

            // The same acknowledgement twice (a page that re-sent it after a reload) keeps nothing twice.
            var existing = new FileInfo(target);
            if (existing.Exists && existing.Length == size)
            {
                TryDelete(stagedPath);
                return new KeepOutcome(new KeptOriginal(fileId, sha256, size), null);
            }

            if (FreeBytes() is { } free && free < MinFreeBytes)
            {
                return new KeepOutcome(
                    null,
                    $"Moins de {MinFreeBytes / (1024L * 1024 * 1024)} Go libres sur le disque de ce PC : l'original n'est "
                    + "pas gardé. La photo est rangée dans le dossier du patient.");
            }

            Prepare();
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(stagedPath, target, overwrite: true);
        }
        catch (UnauthorizedAccessException)
        {
            return new KeepOutcome(
                null,
                "Le dossier des photos originales appartient à un autre compte Windows de ce PC : l'original n'est pas "
                + "gardé ici. La photo est rangée dans le dossier du patient.");
        }
        catch (Exception)
        {
            return new KeepOutcome(
                null, "L'original n'a pas pu être gardé sur ce PC. La photo est rangée dans le dossier du patient.");
        }

        // ⚠️ Kept from here on, whatever the index says: the sheet is the secondary artefact, and a technician who left
        // it open in a spreadsheet must not make the screen say an original that is on disk was not kept.
        WriteIndexLine(OriginalsPathPlanner.IndexLine(fileId, patientName, takenLocal, originalName, size, sha256));
        return new KeepOutcome(new KeptOriginal(fileId, sha256, size), null);
    }

    /// <summary>Writes the index lines a locked sheet held back. Never throws; what still cannot be written waits.</summary>
    public void FlushIndex() => WriteIndexLine(null);

    /// <summary>The originals this store holds — what a copy to the archive disk must cover.</summary>
    public (int Files, long Bytes) Totals()
    {
        var files = 0;
        var bytes = 0L;
        try
        {
            if (!Directory.Exists(Root))
            {
                return (0, 0);
            }

            foreach (var path in Directory.EnumerateFiles(Root, "*.jpg", SearchOption.AllDirectories))
            {
                if (!OriginalsPathPlanner.IsOriginal(Path.GetRelativePath(Root, path)))
                {
                    continue;
                }

                files++;
                bytes += new FileInfo(path).Length;
            }
        }
        catch
        {
            // Unreadable (another account's store): the figures stay what was counted.
        }

        return (files, bytes);
    }

    public long? FreeBytes()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(Root));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Creates the folder with the coffre's protection. Best-effort, as the coffre's own is.</summary>
    public void Prepare()
    {
        if (Directory.Exists(Root))
        {
            return;
        }

        Directory.CreateDirectory(Root);
        ArchiveCopyService.HardenFolder(Root);
    }

    /// <summary>
    /// Appends <paramref name="line"/> (and any held back) to the index, which only grows — a rewrite per photo read
    /// and wrote the whole sheet each time. Held back in memory when the sheet cannot be opened.
    /// </summary>
    private void WriteIndexLine(string? line)
    {
        lock (_indexGate)
        {
            if (line != null)
            {
                _unwrittenLines.Add(line);
            }

            if (_unwrittenLines.Count == 0)
            {
                return;
            }

            try
            {
                var index = Path.Combine(Root, OriginalsPathPlanner.IndexFileName);
                using var stream = new FileStream(index, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                var text = new StringBuilder();
                if (stream.Length == 0)
                {
                    // A BOM, or Excel on Windows reads « Béchir » as « BÃ©chir » (CsvTable's lesson).
                    stream.Write(Encoding.UTF8.GetPreamble());
                    text.Append(OriginalsPathPlanner.IndexHeader).Append("\r\n");
                }
                else
                {
                    // A line cut short by a power cut must not swallow the next one.
                    stream.Seek(-1, SeekOrigin.End);
                    if (stream.ReadByte() != '\n')
                    {
                        text.Append("\r\n");
                    }
                }

                foreach (var held in _unwrittenLines)
                {
                    text.Append(held).Append("\r\n");
                }

                stream.Seek(0, SeekOrigin.End);
                stream.Write(Encoding.UTF8.GetBytes(text.ToString()));
                _unwrittenLines.Clear();
            }
            catch
            {
                // Open in a spreadsheet, most likely: the lines wait for the next original or the next copy.
            }
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
            // The staging sweep takes it.
        }
    }
}
