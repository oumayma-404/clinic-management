using System;
using System.IO;
using System.Text.Json;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// What this machine has been told about the automatic archive copy (<c>clinic-archive-auto-copy</c>).
///
/// <para>⚠️ <b>Per user and per machine, beside <c>server.json</c> and never in the archive folder itself.</b> The
/// grant is a credential; putting it where the copies land would mean every copy carried the key to fetch the
/// next one.</para>
///
/// <para>⚠️ <b>Absent means the feature is off</b>, not misconfigured (AC-10). A shell with no settings behaves
/// exactly as it did before this existed: no scheduler, no folder, no prompt.</para>
/// </summary>
public sealed class ArchiveCopySettings
{
    /// <summary>The folder copies land in. Empty ⇒ the feature is off.</summary>
    public string Folder { get; set; } = "";

    /// <summary>The grant's secret, as pasted from « Postes autorisés ».</summary>
    public string GrantSecret { get; set; } = "";

    /// <summary>How often a copy is wanted. Days, because an archive is a full snapshot — see <see cref="IsDue"/>.</summary>
    public int EveryDays { get; set; } = 7;

    /// <summary>How many copies to keep. Older ones are pruned only after a new one has landed (AC-6).</summary>
    public int KeepCopies { get; set; } = 4;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Folder) && !string.IsNullOrWhiteSpace(GrantSecret);

    /// <summary>
    /// Whether a copy is owed, judged from <b>the newest copy on disk</b> rather than from a stored « last run ».
    ///
    /// <para>⚠️ That is the whole reason there is no alarm and no timer state: a laptop that was closed for a week
    /// must take one copy on the next launch, not miss the window silently, and a stored timestamp would also
    /// disagree with the folder the moment somebody moved or deleted a file. The files are the record.</para>
    /// </summary>
    public bool IsDue(DateTime? newestCopyUtc, DateTime nowUtc) =>
        IsConfigured && (newestCopyUtc == null || newestCopyUtc.Value.AddDays(Math.Max(1, EveryDays)) <= nowUtc);
}

/// <summary>Reads and writes <see cref="ArchiveCopySettings"/>, on <see cref="ServerConfigStore"/>'s shape.</summary>
public static class ArchiveCopySettingsStore
{
    private const string FileName = "archive-copy.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClinicManagement",
            FileName);

    /// <summary>Never throws: an unreadable or corrupt file reads as « not configured », which turns the feature off.</summary>
    public static ArchiveCopySettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new ArchiveCopySettings();
            }

            return JsonSerializer.Deserialize<ArchiveCopySettings>(File.ReadAllText(Path))
                   ?? new ArchiveCopySettings();
        }
        catch
        {
            return new ArchiveCopySettings();
        }
    }

    public static void Save(ArchiveCopySettings settings)
    {
        var path = Path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
    }
}
