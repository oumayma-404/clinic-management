using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ClinicManagement.DesktopShell;

/// <summary>One line of the server's file manifest, as the shell reads it.</summary>
public sealed record MirrorEntry(
    Guid FileId,
    Guid PatientId,
    string PatientName,
    string FileName,
    long FileSize,
    DateTime UploadedAt);

/// <summary>A manifest entry and the relative path it was assigned under <c>fichiers/</c>.</summary>
public sealed record MirrorPlanItem(MirrorEntry Entry, string RelativePath);

/// <summary>
/// Turns a manifest into the folder tree it will occupy (<c>patient-file-mirror</c>, AC-3 and AC-10).
///
/// <para>⚠️ <b>Pure, and a function of the WHOLE manifest rather than of one entry.</b> That is what makes the
/// mirror diffable with no bookkeeping file beside it: given the same manifest, any machine computes the same
/// paths, so « do I already have this file? » is answered by looking at the disk and nothing else. It is also why
/// the collision rules below cannot be applied one row at a time — whether « Ben Salah, Amine » needs its id
/// suffix is a fact about the other rows, not about that row.</para>
///
/// <para>⚠️ <b>A collision suffixes BOTH sides, never one.</b> Suffixing only the second arrival would make a
/// path depend on the order the pages happened to arrive in, and the first file would silently keep the plain
/// name it no longer unambiguously owns.</para>
/// </summary>
public static class MirrorPathPlanner
{
    /// <summary>The subfolder the mirror owns, beside the archive's own <c>archive-*.zip</c> files.</summary>
    public const string RootFolderName = "fichiers";

    /// <summary>
    /// Windows' real ceiling is 260 including the drive and the terminating null. The margin absorbs the
    /// destination folder the user chose, which this planner never sees.
    /// </summary>
    private const int MaxRelativePathLength = 120;

    private static readonly char[] Invalid =
        Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }).Distinct().ToArray();

    /// <summary>
    /// Device names Windows still refuses as a file name in 2026, with or without an extension — a patient
    /// genuinely called « Aux » would otherwise produce a folder that cannot be created, and the run would fail
    /// on a name rather than on anything to do with the mirror.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static IReadOnlyList<MirrorPlanItem> Plan(IReadOnlyCollection<MirrorEntry> manifest)
    {
        // Which patient NAMES are shared by more than one patient. Only those get an id suffix, so the common
        // cabinet keeps folders a human can read while two people called « Ben Ali, Mohamed » stay apart.
        var ambiguousNames = manifest
            .GroupBy(e => Sanitise(e.PatientName), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(e => e.PatientId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var folders = new Dictionary<Guid, string>();
        foreach (var entry in manifest)
        {
            if (folders.ContainsKey(entry.PatientId))
            {
                continue;
            }

            var name = Sanitise(entry.PatientName);
            if (name.Length == 0)
            {
                // A patient whose name sanitises away is still mirrored — under their id, which is ugly and
                // findable, rather than dropped, which is neither (AC-10).
                name = "Patient " + Short(entry.PatientId);
            }
            else if (ambiguousNames.Contains(name))
            {
                name = $"{name} ({Short(entry.PatientId)})";
            }

            folders[entry.PatientId] = name;
        }

        // Within one patient's folder, the plain « date - nom » of two entries can still coincide: the same
        // document scanned twice on the same day under the same name. Both are then suffixed.
        var ambiguousFiles = manifest
            .GroupBy(e => (e.PatientId, Plain: PlainFileName(e)), TupleComparer)
            .Where(g => g.Select(e => e.FileId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(TupleComparer);

        var plan = new List<MirrorPlanItem>(manifest.Count);
        foreach (var entry in manifest)
        {
            var folder = folders[entry.PatientId];
            var plain = PlainFileName(entry);

            var file = ambiguousFiles.Contains((entry.PatientId, plain))
                ? Suffixed(entry)
                : plain;

            plan.Add(new MirrorPlanItem(entry, Fit(folder, file)));
        }

        return plan;
    }

    private static readonly IEqualityComparer<(Guid PatientId, string Plain)> TupleComparer =
        new PatientFileNameComparer();

    private sealed class PatientFileNameComparer : IEqualityComparer<(Guid PatientId, string Plain)>
    {
        public bool Equals((Guid PatientId, string Plain) a, (Guid PatientId, string Plain) b) =>
            a.PatientId == b.PatientId && string.Equals(a.Plain, b.Plain, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid PatientId, string Plain) value) =>
            HashCode.Combine(value.PatientId, value.Plain.ToLowerInvariant());
    }

    /// <summary>« 2026-08-28 - pano.jpg ». The date leads so a folder sorts chronologically in the Explorer.</summary>
    private static string PlainFileName(MirrorEntry entry)
    {
        var name = Sanitise(entry.FileName);
        if (name.Length == 0)
        {
            name = "fichier";
        }

        return $"{entry.UploadedAt.ToLocalTime():yyyy-MM-dd} - {name}";
    }

    private static string Suffixed(MirrorEntry entry)
    {
        var plain = PlainFileName(entry);
        var extension = Path.GetExtension(plain);
        var stem = plain[..^extension.Length];
        return $"{stem} ({Short(entry.FileId)}){extension}";
    }

    /// <summary>
    /// Keeps the whole relative path inside <see cref="MaxRelativePathLength"/> by shortening the <b>file</b>,
    /// never the folder: two patients' trees must not be able to collapse into one because a name was long.
    /// The extension is preserved, or the mirrored file stops opening in the tool that reads it.
    /// </summary>
    private static string Fit(string folder, string file)
    {
        if (folder.Length > MaxRelativePathLength / 2)
        {
            folder = folder[..(MaxRelativePathLength / 2)].TrimEnd(' ', '.');
        }

        var room = MaxRelativePathLength - folder.Length - 1;
        if (file.Length > room)
        {
            var extension = Path.GetExtension(file);
            var stem = file[..^extension.Length];
            var keep = Math.Max(1, room - extension.Length);
            file = stem[..Math.Min(stem.Length, keep)].TrimEnd(' ', '.') + extension;
        }

        return Path.Combine(folder, file);
    }

    private static string Short(Guid id) => id.ToString("N")[..8];

    /// <summary>
    /// Windows-safe, and deliberately lossy: an unusable character becomes a space rather than disappearing, so
    /// « Ben Salah/Amine » does not silently read as one word.
    /// </summary>
    private static string Sanitise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            builder.Append(Invalid.Contains(c) || char.IsControl(c) ? ' ' : c);
        }

        // Collapse the runs the replacement above creates, then drop the trailing dots and spaces Windows
        // silently strips — a folder created as « Dupont. » is opened as « Dupont », and the next run would not
        // recognise its own work.
        var collapsed = string.Join(
            ' ',
            builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        collapsed = collapsed.TrimEnd(' ', '.');

        return Reserved.Contains(Path.GetFileNameWithoutExtension(collapsed))
            ? "_" + collapsed
            : collapsed;
    }
}
