using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// Where a kept original lives, and the line that names it in <c>index.csv</c> (<c>features/camera-photo-import</c>,
/// phase 2) — pure, <see cref="MirrorPathPlanner"/>'s shape.
///
/// <para>⚠️ <b>Keyed by the file's id, never by the patient or the date.</b> « Changer de patient » moves a photo
/// between patients and a fiche correction moves its date; a path built from either would have to be re-planned on
/// disk each time (the mirror's problem), while an id never changes. <c>{id[0..2]}\{id}.jpg</c> keeps any one folder
/// to a few hundred files, which Explorer opens without stalling.</para>
///
/// <para>⚠️ <b><c>index.csv</c> is what makes the folder readable without the app</b> — a practice that stops using
/// it, or a technician restoring a disk, finds the patient, the time and the camera's own file name beside each id.
/// Same rules as every CSV the product writes: <c>;</c>, quoted where needed, a cell a spreadsheet would execute
/// neutralised.</para>
/// </summary>
public static class OriginalsPathPlanner
{
    public const string IndexFileName = "index.csv";

    public const string IndexHeader = "fichier;patient;prise de vue;nom d'origine;taille (octets);sha256";

    private const char Delimiter = ';';

    public static string RelativePathFor(Guid fileId)
    {
        var id = fileId.ToString("D");
        return Path.Combine(id[..2], id + ".jpg");
    }

    /// <summary>Whether a path relative to the store's root is a kept original — what a copy report counts.</summary>
    public static bool IsOriginal(string relativePath)
    {
        var parts = relativePath.Replace('/', '\\').Split('\\');
        if (parts.Length != 2 || !parts[1].EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stem = parts[1][..^4];
        return Guid.TryParseExact(stem, "D", out var id)
               && string.Equals(parts[0], id.ToString("D")[..2], StringComparison.OrdinalIgnoreCase);
    }

    public static string IndexLine(
        Guid fileId, string? patientName, DateTime? takenLocal, string originalName, long sizeBytes, string sha256) =>
        string.Join(
            Delimiter,
            fileId.ToString("D"),
            Cell(patientName),
            takenLocal?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
            Cell(originalName),
            sizeBytes.ToString(CultureInfo.InvariantCulture),
            sha256);

    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // A patient named « =WEBSERVICE(…) » must not execute in the sheet a technician opens.
        if ("=+@\t\r".Contains(value[0]) || (value[0] == '-' && !decimal.TryParse(value, out _)))
        {
            value = "'" + value;
        }

        var needsQuotes = value.Contains(Delimiter) || value.Contains('"') || value.Contains('\n')
                          || value.Contains('\r') || value != value.Trim();
        return needsQuotes ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }
}
