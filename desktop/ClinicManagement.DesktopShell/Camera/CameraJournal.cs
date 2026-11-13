using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>One photo the journal knows about.</summary>
public sealed class CameraJournalEntry
{
    public string ItemId { get; set; } = "";

    public string Key { get; set; } = "";

    public string SourceKey { get; set; } = "";

    public string Name { get; set; } = "";

    public long Size { get; set; }

    public DateTime? TakenLocal { get; set; }

    public CameraItemState State { get; set; }

    /// <summary>The copy on this PC the page reads, until the photo is filed (and its original kept, or not).</summary>
    public string? StagedPath { get; set; }

    /// <summary>Hashed while copying off the source, so keeping the original never reads it twice.</summary>
    public string? Sha256 { get; set; }

    public Guid? FileId { get; set; }

    public Guid? PatientId { get; set; }

    public bool OriginalKept { get; set; }

    /// <summary>The server has written down that this PC holds the original (« Original : PC ACCUEIL »).</summary>
    public bool OriginalRecorded { get; set; }

    public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// What this PC has done with each photo it has seen on a card or a camera (<c>features/camera-photo-import</c>,
/// phase 2) — so re-inserting a card offers only what is new, and a batch the dentist walked away from is still
/// there tomorrow (« en attente »).
///
/// <para>⚠️ <b>Per Windows account, in <c>%LocalAppData%</c></b>, not in the shared photos folder: that folder is
/// locked to the account that created it (the coffre's ACL), and a journal another account cannot write would make
/// the import fail outright for the second person at the PC. The server's capture key is what really prevents a
/// photo being filed twice; the journal only spares the copying.</para>
///
/// <para>⚠️ <b>Every write lands whole</b> (a temp file, then a replace): a journal cut in half by a power cut would
/// read as empty, and the next card would be offered from scratch — which is safe, only slow.</para>
/// </summary>
public sealed class CameraJournal
{
    public const int KeepDoneDays = 180;
    public const int KeepPendingDays = 7;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, CameraJournalEntry> _byKey;

    public CameraJournal(string path)
    {
        _path = path;
        _byKey = Load(path);
    }

    public static string DefaultPath =>
        Path.Combine(ShellProfile.Local, "ClinicManagement", "camera-journal.json");

    /// <summary>Card or device · place on it · size · date — a reformatted card reusing a name is a different photo.</summary>
    public static string KeyOf(string sourceKey, CameraItem item) =>
        string.Join(
            '|',
            sourceKey,
            item.ObjectId.Replace('\\', '/'),
            item.Size.ToString(CultureInfo.InvariantCulture),
            item.TakenLocal?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "-");

    /// <summary>Sixteen hex characters — what the page acknowledges by, and nothing it could reverse.</summary>
    public static string ItemIdOf(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 8).ToLowerInvariant();

    public static bool IsItemId(string? value) =>
        value is { Length: 16 } && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    public CameraItemState? StateOf(string sourceKey, CameraItem item)
    {
        lock (_gate)
        {
            return _byKey.TryGetValue(KeyOf(sourceKey, item), out var entry) ? entry.State : null;
        }
    }

    public CameraJournalEntry? ByItemId(string itemId)
    {
        lock (_gate)
        {
            return _byKey.Values.FirstOrDefault(e => e.ItemId == itemId) is { } entry ? Clone(entry) : null;
        }
    }

    /// <summary>The newest photo of this source already dealt with — where the next look at it starts.</summary>
    public DateTime? NewestDoneLocal(string sourceKey)
    {
        lock (_gate)
        {
            return _byKey.Values
                .Where(e => e.SourceKey == sourceKey && CameraBatchPlanner.IsDone(e.State) && e.TakenLocal != null)
                .Select(e => e.TakenLocal)
                .Max();
        }
    }

    /// <summary>Adds or updates the entry for one listed item.</summary>
    public CameraJournalEntry Record(
        string sourceKey, CameraItem item, CameraItemState state, string? stagedPath, string? sha256, DateTime nowUtc)
    {
        lock (_gate)
        {
            var key = KeyOf(sourceKey, item);
            if (!_byKey.TryGetValue(key, out var entry))
            {
                entry = new CameraJournalEntry
                {
                    ItemId = ItemIdOf(key),
                    Key = key,
                    SourceKey = sourceKey,
                    Name = item.Name,
                    Size = item.Size,
                    TakenLocal = item.TakenLocal,
                };
                _byKey[key] = entry;
            }

            entry.State = state;
            entry.StagedPath = stagedPath;
            entry.Sha256 = sha256 ?? entry.Sha256;
            entry.UpdatedUtc = nowUtc;
            return Clone(entry);
        }
    }

    /// <summary>Changes one entry in place. False when the journal does not know the id.</summary>
    public bool Update(string itemId, Action<CameraJournalEntry> change, DateTime nowUtc)
    {
        lock (_gate)
        {
            var entry = _byKey.Values.FirstOrDefault(e => e.ItemId == itemId);
            if (entry is null)
            {
                return false;
            }

            change(entry);
            entry.UpdatedUtc = nowUtc;
            return true;
        }
    }

    /// <summary>Photos copied here and handed over, never answered — offered again until they are.</summary>
    public IReadOnlyList<CameraJournalEntry> Pending()
    {
        lock (_gate)
        {
            return _byKey.Values
                .Where(e => !CameraBatchPlanner.IsDone(e.State) && !string.IsNullOrEmpty(e.StagedPath))
                .OrderBy(e => e.TakenLocal ?? DateTime.MaxValue)
                .Select(Clone)
                .ToList();
        }
    }

    /// <summary>Originals kept on this PC that the server has not written down yet.</summary>
    public IReadOnlyList<CameraJournalEntry> UnrecordedOriginals()
    {
        lock (_gate)
        {
            return _byKey.Values
                .Where(e => e.OriginalKept && !e.OriginalRecorded && e.FileId != null && e.PatientId != null)
                .Select(Clone)
                .ToList();
        }
    }

    /// <summary>
    /// Forgets what no longer matters and hands back what was forgotten, so the caller deletes their copies: a photo
    /// dealt with more than <see cref="KeepDoneDays"/> days ago (older than any window a card is read with), and one
    /// left unanswered for <see cref="KeepPendingDays"/> days (its copy goes; the card still has it).
    /// </summary>
    public IReadOnlyList<CameraJournalEntry> Prune(DateTime nowUtc)
    {
        lock (_gate)
        {
            var removed = _byKey.Values
                .Where(e => CameraBatchPlanner.IsDone(e.State)
                    ? e.UpdatedUtc < nowUtc.AddDays(-KeepDoneDays) && !(e.OriginalKept && !e.OriginalRecorded)
                    : e.UpdatedUtc < nowUtc.AddDays(-KeepPendingDays))
                .ToList();

            foreach (var entry in removed)
            {
                _byKey.Remove(entry.Key);
            }

            return removed.Select(Clone).ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byKey.Count;
            }
        }
    }

    /// <summary>Writes the journal whole. Never throws: a journal that could not be saved costs a re-copy, nothing more.</summary>
    public void Save()
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = $"{_path}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_byKey.Values.ToList(), Json));
                File.Move(temp, _path, overwrite: true);
            }
            catch
            {
                // A full disk or a locked file: the next save tries again with everything still in memory.
            }
        }
    }

    private static Dictionary<string, CameraJournalEntry> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var entries = JsonSerializer.Deserialize<List<CameraJournalEntry>>(File.ReadAllText(path), Json);
                if (entries != null)
                {
                    return entries
                        .Where(e => !string.IsNullOrEmpty(e.Key))
                        .GroupBy(e => e.Key)
                        .ToDictionary(g => g.Key, g => g.Last());
                }
            }
        }
        catch
        {
            // Corrupt or unreadable: start empty. Every photo is offered again and the server refuses the ones it has.
        }

        return new Dictionary<string, CameraJournalEntry>();
    }

    private static CameraJournalEntry Clone(CameraJournalEntry entry) => new()
    {
        ItemId = entry.ItemId,
        Key = entry.Key,
        SourceKey = entry.SourceKey,
        Name = entry.Name,
        Size = entry.Size,
        TakenLocal = entry.TakenLocal,
        State = entry.State,
        StagedPath = entry.StagedPath,
        Sha256 = entry.Sha256,
        FileId = entry.FileId,
        PatientId = entry.PatientId,
        OriginalKept = entry.OriginalKept,
        OriginalRecorded = entry.OriginalRecorded,
        UpdatedUtc = entry.UpdatedUtc,
    };
}
